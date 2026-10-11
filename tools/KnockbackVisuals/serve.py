"""Serve the saved viewer from any working directory. Python standard library only."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    folder = Path(__file__).resolve().parents[2] / 'docs/generated/knockback-visuals'
    if not (folder / 'index.html').is_file():
        parser.error('Viewer is missing. Run tools/KnockbackVisuals/generate.py first.')
    try:
        server = ThreadingHTTPServer(('127.0.0.1', args.port),
                                     partial(SimpleHTTPRequestHandler, directory=str(folder)))
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
