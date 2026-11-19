"""Leitor de respostas da main. Cada execução reapresenta o histórico."""
from pathlib import Path
from materializar import save
import argparse
import re
import subprocess
import sys
import tempfile
import time

REMOTE = "https://github.com/alexbonemberger/chat-automation-test_id_0000.git"
NAME = re.compile(r"responses/[0-9]{4}\.(?:txt|md)")


def git(directory, *args):
    result = subprocess.run(["git", "-C", directory, *args], capture_output=True, timeout=45)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors="replace").strip())
    return result.stdout


def poll(directory, remote, seen):
    git(directory, "fetch", "--quiet", "--depth=1", remote, "refs/heads/main")
    head = git(directory, "rev-parse", "FETCH_HEAD").decode().strip()
    listing = git(directory, "ls-tree", "-r", head, "--", "responses").decode()
    for entry in listing.splitlines():
        meta, name = entry.split("\t", 1)
        mode, kind, oid = meta.split()
        if mode != "100644" or kind != "blob" or not NAME.fullmatch(name) or seen.get(name) == oid:
            continue
        raw = git(directory, "cat-file", "blob", oid)
        message = raw.decode("utf-8")
        target = save(Path(__file__).resolve().parent / "recebidos", name, head, oid, raw)
        print(f"[{name}] commit={head} blob={oid}", flush=True)
        print(message.rstrip("\r\n"), flush=True)
        print(f"GRAVADO={target}", flush=True)
        if name == "responses/0003.md":
            prompt = target.parents[2] / "evidencias" / "0003-prompt.txt"
            if prompt.is_file():
                expected = re.search(r"CHAT_DESTINO_[0-9a-f]{32}", prompt.read_text(encoding="utf-8-sig"))
                matched = expected and message.splitlines()[:1] == [expected.group()] and any(target.glob("codigo/*.cs"))
                print("CICLO_0003=" + ("PASS" if matched else "DIVERGENTE"), flush=True)
        seen[name] = oid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    seen = {}
    with tempfile.TemporaryDirectory(prefix="chat-git-") as directory:
        git(directory, "init", "--bare", "--quiet")
        print("Lendo responses/ na main. Ctrl+C para parar.", flush=True)
        while True:
            try:
                poll(directory, REMOTE, seen)
            except (RuntimeError, subprocess.TimeoutExpired, UnicodeError, OSError) as error:
                if args.once:
                    raise
                print(f"Falha na leitura: {error}", file=sys.stderr, flush=True)
            if args.once:
                if not seen:
                    raise RuntimeError("Nenhuma resposta encontrada na main.")
                return
            time.sleep(8)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
