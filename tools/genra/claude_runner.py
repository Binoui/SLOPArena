"""One bounded, sandboxed Claude Code CLI job (the authoring provider).

Saved Claude login with API-key variables removed; no user/project settings, MCP
servers, skills, auto-memory or session persistence; an empty scratch directory outside
the repository. Bash is pre-approved but runs in the bubblewrap sandbox: it can read only
the listed paths inside the repository, write nowhere in the repository (only the scratch
directory and Claude's temp directory), and has no network. (dontAsk mode without the Bash allow rule refuses
heredocs and piped Python.)
"""
from __future__ import annotations

import base64
import json
import os
import shutil
import signal
import subprocess
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def settings(readable) -> dict:
    return {'autoMemoryEnabled': False,
            'permissions': {'allow': ['Bash']},
            'sandbox': {'enabled': True, 'autoAllowBashIfSandboxed': True, 'allowUnsandboxedCommands': False,
                        # denyWrite also covers --add-dir paths, which the sandbox otherwise treats as writable.
                        'filesystem': {'denyRead': [str(REPO)], 'allowRead': [str(p) for p in readable],
                                       'denyWrite': [str(REPO)]}}}


def environment(max_output_tokens='128000', cache_5m=False):
    env = {k: v for k, v in os.environ.items() if not k.startswith(('CLAUDE', 'ANTHROPIC'))}
    env['CLAUDE_CODE_MAX_OUTPUT_TOKENS'] = max_output_tokens
    if cache_5m:  # short, frequent turns keep a 5-minute cache warm; writes cost 1.25x input instead of 2x
        env['FORCE_PROMPT_CACHING_5M'] = '1'
    return env, sorted(k for k in os.environ if k.startswith(('CLAUDE', 'ANTHROPIC')))


def message(prompt, images) -> str:
    content = [{'type': 'image', 'source': {'type': 'base64', 'media_type': 'image/png',
                'data': base64.b64encode(Path(p).read_bytes()).decode()}} for p in images]
    content.append({'type': 'text', 'text': prompt})
    return json.dumps({'type': 'user', 'message': {'role': 'user', 'content': content}}) + '\n'


def run(prompt, images, scratch, evidence, readable, read_dirs, model='claude-opus-5-5', effort='medium',
        tools='Bash,Read,Grep,Glob', timeout=1800, cache_5m=False, schema=None,
        progress=None, progress_timeout=1200):
    """Run one job in `scratch`. Returns (returncode or None on timeout, final result event or None). With a JSON
    `schema`, the reply is validated against it and the result event carries it as `structured_output`."""
    if timeout <= 0 or (progress is not None and progress_timeout <= 0):
        raise ValueError('Timeouts must be positive seconds')
    scratch, evidence = Path(scratch), Path(evidence)
    settings_path = evidence / 'claude-settings.json'
    settings_path.write_text(json.dumps(settings(readable), indent=2))
    argv = [shutil.which('claude'), '-p', '--model', model, '--effort', effort,
            '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose',
            '--no-session-persistence', '--restricted', '--strict-mcp-config', '--disable-slash-commands',
            '--tools', tools, '--permission-mode', 'dontAsk', '--permission-prompts', 'none', '--settings', str(settings_path)]
    for d in read_dirs:
        argv += ['--add-dir', str(d)]
    if schema is not None:
        argv += ['--json-schema', json.dumps(schema)]
    env, removed = environment(cache_5m=cache_5m)
    (evidence / 'claude-command.json').write_text(json.dumps({
        'argv': argv, 'cwd': str(scratch), 'timeoutSeconds': timeout, 'images': [str(p) for p in images],
        'environmentPolicy': 'saved Claude login; variable names removed below; values never serialized',
        'removedEnvironmentVariables': removed, 'promptCache5m': cache_5m}, indent=2))
    payload = message(prompt, images)  # validate images before starting a provider process
    events = evidence / 'claude-events.jsonl'
    with events.open('w') as out, (evidence / 'claude-process.log').open('w') as err:
        process = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=out, stderr=err, text=True, cwd=scratch,
                                   env=env, start_new_session=True)
        deadline, last_progress = time.monotonic() + timeout, time.monotonic()
        try:
            while True:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    code = None
                    break
                try:
                    process.communicate(payload, timeout=min(5, remaining) if progress else remaining)
                    code = process.returncode
                    break
                except subprocess.TimeoutExpired:
                    payload = None  # communicate resumes the same stdin, never resubmit the prompt
                    if progress:
                        if progress():
                            last_progress = time.monotonic()
                        if time.monotonic() - last_progress >= progress_timeout:
                            err.write(f'No changed, loadable checkpoint for {progress_timeout} seconds.\n')
                            code = 124
                            break
        except KeyboardInterrupt:
            code = 130
        finally:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass  # the provider exited between poll and kill
                process.wait()
    result = None
    for line in events.read_text().splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if event.get('type') == 'result':
            result = event
    return code, result


def tool_calls(events_path) -> list[dict]:
    calls, results = [], {}
    for line in Path(events_path).read_text().splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        body = event.get('message')
        content = body.get('content') if isinstance(body, dict) else None
        for block in content if isinstance(content, list) else []:
            if not isinstance(block, dict):
                continue
            if block.get('type') == 'tool_use':
                calls.append({'id': block['id'], 'tool': block['name'], 'input': block.get('input')})
            elif block.get('type') == 'tool_result':
                inner = block.get('content')
                text = inner if isinstance(inner, str) else json.dumps(inner)
                results[block.get('tool_use_id')] = {'isError': bool(block.get('is_error')), 'resultHead': text[:300]}
    for call in calls:
        call.update(results.get(call['id'], {'isError': None, 'resultHead': None}))
    return calls
