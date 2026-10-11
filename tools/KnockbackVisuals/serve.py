"""Serve the saved viewer from any working directory. Python standard library only."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--view', choices=['original', 'outliers'], help='Select the initial comparison')
    args = parser.parse_args()
    folder = Path(__file__).resolve().parents[2] / 'docs/generated'
    view = args.view or ('outliers' if (folder / 'knockback-outlier-visuals/index.html').exists() else 'original')
    page = 'knockback-outlier-visuals' if view == 'outliers' else 'knockback-visuals'
    if not (folder / page / 'index.html').is_file():
        parser.error('Viewer is missing. Run tools/KnockbackVisuals/generate.py first.')
    class Handler(SimpleHTTPRequestHandler):
        def do_GET(self):
            if self.path == '/':
                self.send_response(302)
                self.send_header('Location', f'/{page}/')
                self.end_headers()
            else:
                super().do_GET()
    try:
        server = ThreadingHTTPServer(('127.0.0.1', args.port),
                                     partial(Handler, directory=str(folder)))
    except OSError as error:
        parser.error(f'{error}. If the port is busy, choose another with --port 8766.')
    with server:
        print(f'Knockback viewer: http://localhost:{server.server_port}/', flush=True)
        print('Ctrl+C to stop. You can also open index.html directly in a browser.', flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == '__main__':
    main()
