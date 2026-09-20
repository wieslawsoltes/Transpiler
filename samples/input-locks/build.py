#!/usr/bin/env python3
"""Emit real PE, record both target locks, relocate the closure and verify replay."""
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(__file__).resolve().parent
CLI = ROOT / 'src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll'
OUT = ROOT / 'artifacts/input-lock-demo'


def run(*args):
    return subprocess.run([str(arg) for arg in args], check=True, capture_output=True,
                          text=True, encoding='utf-8', cwd=ROOT, timeout=60).stdout


def compile_cli(*args):
    return run('dotnet', CLI, *args)


def main():
    if not CLI.is_file():
        raise SystemExit('Build Transpiler.slnx in Release first.')
    leaf, middle, relocated = (OUT / name for name in ('leaf', 'middle', 'relocated'))
    for directory in (leaf, middle, relocated):
        directory.mkdir(parents=True, exist_ok=True)
    arithmetic, api, app = leaf / 'Arithmetic.dll', middle / 'Api.dll', OUT / 'Program.dll'
    compile_cli('emit-pe', SOURCE / 'Arithmetic.cs', '--library', '--out', arithmetic)
    compile_cli('emit-pe', SOURCE / 'Api.cs', '--library', '--reference', arithmetic, '--out', api)
    compile_cli('emit-pe', SOURCE / 'Program.cs', '--reference', api, '--reference', arithmetic, '--out', app)
    for path in (arithmetic, api, app):
        shutil.copy2(path, relocated / path.name)
    for target, extension, runtime in (('js', 'mjs', 'node'), ('py', 'py', sys.executable)):
        output, lock = OUT / ('app.' + extension), OUT / (target + '.inputs.json')
        compile_cli('compile', app, '--reference-dir', leaf, '--reference-dir', middle,
                    '--target', target, '--out', output, '--write-input-lock', lock)
        original = output.read_bytes()
        compile_cli('compile', relocated / app.name, '--reference-dir', relocated,
                    '--target', target, '--out', output, '--input-lock', lock,
                    '--manifest', OUT / (target + '.manifest.json'))
        if output.read_bytes() != original:
            raise AssertionError('Relocated emission changed: ' + target)
        if run(runtime, output) != '42\n':
            raise AssertionError('Unexpected application result: ' + target)
        print(target + ': relocated locked replay passed; output=42')


if __name__ == '__main__':
    main()
