import hashlib
import json
from pathlib import Path
import re
import shutil
import tempfile
from datetime import datetime, timezone

EXT = {"python":"py", "csharp":"cs", "cs":"cs", "javascript":"js", "json":"json",
       "powershell":"ps1", "html":"html", "css":"css", "typescript":"ts", "bash":"sh"}
OPEN = re.compile(r"^ {0,3}(`{3,}|~{3,})([^\r\n]*)$")


def blocks(message):
    fence = None
    for line in message.splitlines(keepends=True):
        clean = line.rstrip("\r\n")
        if fence is None:
            match = OPEN.fullmatch(clean)
            if match:
                fence, info = match.groups()
                language = info.strip().lower().split()[:1]
                extension = EXT.get(next(iter(language), ""), "txt")
                body = []
        elif re.fullmatch(r" {0,3}" + fence[0] + "{"+str(len(fence))+",}[ \t]*", clean):
            yield extension, "".join(body).encode()
            fence = None
        else:
            body.append(line)


def save(root, name, head, oid, raw):
    parent = root / Path(name).stem
    parent.mkdir(parents=True, exist_ok=True)
    target = parent / oid
    if target.is_dir():
        return target
    stage = Path(tempfile.mkdtemp(prefix=".tmp-", dir=parent))
    try:
        files = {}
        def write(relative, data):
            output = stage / relative
            output.parent.mkdir(exist_ok=True)
            output.write_bytes(data)
            files[relative] = hashlib.sha256(data).hexdigest()
        write("resposta" + Path(name).suffix, raw)
        for index, (ext, data) in enumerate(blocks(raw.decode("utf-8")), 1):
            write(f"codigo/bloco-{index:02}.{ext}", data)
        receipt = dict(origem=name, commit=head, blob=oid, arquivos_sha256=files,
                       observado_utc=datetime.now(timezone.utc).isoformat())
        (stage / "recibo.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
        try:
            stage.rename(target)
        except OSError:
            if not target.is_dir():
                raise
        return target
    finally:
        if stage.exists():
            shutil.rmtree(stage)
