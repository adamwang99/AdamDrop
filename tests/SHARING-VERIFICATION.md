# PC to iPhone implementation verification

Production build: `python build.py`, compiler exit 0, AdamDrop.exe 911360 bytes.
Restored production server: listening True, lang en, live uploads 0, activeDownloads 0, sharing queue 0 before parent native-picker check.

Command: `python -u tests/run-sharing.py`
Final successful isolated artifacts: `C:\Users\adam\AppData\Local\Temp\adamdrop-sharing-kjuavyqh`
All integration assertions passed:
- Five deliberately selected fixtures, no PC paths in phone JSON.
- Full download equal bytes, SHA256 aca6f4d81a88030dc3e4b99988449ba2943885a56a5ebda5be275f64149677fe.
- UTF8 filename including Unicode extension.
- Range 3-57, suffix -19, open end 1048570-: 206 exact bytes.
- Out-of-bounds, zero suffix, reversed and multiple range: 416. Malformed bytes=oops rejected by HTTP.sys with 400.
- If-Range fallback 200 full bytes; HEAD empty body.
- Empty, HTML, SVG, PNG fixtures exact bytes; nosniff; only safe PNG inline, others attachment.
- Wrong phone key 403, foreign Host 403, admin GET 405, missing admin token 403, foreign Origin 403.
- Existing raw upload regression exact bytes in isolated receive folder.
- Remove revokes old ID 404 and list readback; stop empties list; repeated wrong key reaches 429.

Both embedded JS scripts pass node --check. Dashboard duplicate named JS functions [] and six data-tab attributes.
Test runner final safety improvement refuses if port 8765 occupied. Verified refusal while production is running, without mutating it. Test process always terminates; removed --keep option.

No test uploads/history were written into production. Tests use a separate executable BaseDirectory; port 8765 is reused only while real app was stopped after idle check because Windows HTTP.sys has no reservation for 18765. Normal app restored afterward. No URLACL/firewall changes.

Parent is performing independent production native multi-select, DOM, screenshot, mobile and stop-sharing checks. This report does not claim those passed yet. Real iPhone Safari Files/Photos save remains manual verification. Image/video format support depends on iOS. Sharing removal cannot recall already downloaded copies.

Known environment notes: not a git repository. Framework csc needs Windows backslash absolute source paths (forward-slash inputs were misparsed). Legacy temp build shell masked csc failures; now set -euo pipefail and no compiler output pipeline. Production build used checked Python subprocess.
