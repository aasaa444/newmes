# -*- coding: utf-8 -*-
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1] / "src/Mes.Api"
englishish = []
for f in root.rglob("*.cs"):
    text = f.read_text(encoding="utf-8")
    for i, line in enumerate(text.splitlines(), 1):
        if "InvalidOperationException" not in line and "error =" not in line:
            continue
        if re.search(r'"(?:product serial|work order|cannot |only |issue |serial |material |BOM |anti-skip)', line):
            englishish.append(f"{f.name}:{i}: {line.strip()}")

print("remaining english-looking user errors:", len(englishish))
for x in englishish[:30]:
    print(x)

# spot-check key Chinese
sp = (root / "Execution/StationPassService.cs").read_text(encoding="utf-8")
assert "请先领料再绑定关键件" in sp
assert "防跳站" in sp
print("spot-check OK")
