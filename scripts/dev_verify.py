#!/usr/bin/env python3
"""开发验证工作流：build + test + diff check → 生成 dev-log 记录（md + html）

用法：
    python scripts/dev_verify.py                    # 自动检测 HEAD
    python scripts/dev_verify.py -t "延迟截图"       # 自定义标题
    python scripts/dev_verify.py -c abc1234          # 指定 commit
"""

import argparse
import html
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path


def run(cmd, cwd=None):
    """Run a command, return (returncode, stdout+stderr)."""
    result = subprocess.run(
        cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    output = result.stdout + result.stderr
    return result.returncode, output.strip()


def collect_commit_info(root: Path, commit: str) -> dict:
    info = {}
    info["hash"] = run(["git", "rev-parse", "--short", commit], root)[1]
    info["message"] = run(["git", "log", "-1", "--format=%s", commit], root)[1]
    info["author"] = run(["git", "log", "-1", "--format=%an", commit], root)[1]
    info["date"] = run(["git", "log", "-1", "--format=%ai", commit], root)[1]
    rc, out = run(["git", "diff-tree", "--no-commit-id", "--name-status", "-r", commit], root)
    info["files"] = [line for line in out.splitlines() if line.strip()] if rc == 0 else []
    return info


def build(root: Path) -> tuple[bool, str]:
    print("-- BUILD --", flush=True)
    rc, out = run(["dotnet", "build", str(root / "src/Index/Index.csproj"), "--no-restore"], root)
    success = rc == 0
    print(f"  Result: {'PASS' if success else 'FAIL'}", flush=True)
    return success, out


def test(root: Path) -> tuple[bool, str]:
    print("-- TEST --", flush=True)
    rc, out = run(
        ["dotnet", "test", str(root / "src/Index.Tests/Index.Tests.csproj"), "--no-restore"], root
    )
    success = rc == 0
    # extract stats line
    stats = ""
    for line in out.splitlines():
        if "passed" in line or "failed" in line:
            stats = line.strip()
    print(f"  Result: {'PASS' if success else 'FAIL'}", flush=True)
    if stats:
        print(f"  {stats}", flush=True)
    return success, out


def diff_check(root: Path) -> tuple[bool, str]:
    print("-- DIFF CHECK --", flush=True)
    rc, out = run(["git", "diff", "--check"], root)
    success = rc == 0 and not out
    print(f"  Result: {'PASS' if success else 'FAIL'}", flush=True)
    return success, out


def generate_markdown(
    title: str,
    info: dict,
    build_ok: bool,
    test_ok: bool,
    diff_ok: bool,
    build_out: str,
    test_out: str,
    diff_out: str,
    verify_time: str,
) -> str:
    all_passed = build_ok and test_ok and diff_ok
    overall = "PASS" if all_passed else "FAIL"

    file_table = "\n".join(f"| {f} |" for f in info["files"]) if info["files"] else "| (none) |"

    return f"""# {title}

| Item | Value |
|---|---|
| Commit | `{info['hash']}` |
| Author | {info['author']} |
| Commit Time | {info['date']} |
| Verify Time | {verify_time} |
| Build | {'PASS' if build_ok else 'FAIL'} |
| Test | {'PASS' if test_ok else 'FAIL'} |
| Diff Check | {'PASS' if diff_ok else 'FAIL'} |
| **Overall** | **{overall}** |

## Changed Files

| File |
|---|
{file_table}

## Verification Checklist

- [ ] Core functionality works
- [ ] Edge cases handled
- [ ] No regressions
- [ ] Ready to push

## Build Output

```
{build_out}
```

## Test Output

```
{test_out}
```

## Diff Check

```
{diff_out}
```
"""


def generate_html(
    title: str,
    info: dict,
    build_ok: bool,
    test_ok: bool,
    diff_ok: bool,
    build_out: str,
    test_out: str,
    diff_out: str,
    verify_time: str,
) -> str:
    all_passed = build_ok and test_ok and diff_ok

    def badge(ok: bool) -> str:
        cls = "badge-pass" if ok else "badge-fail"
        text = "PASS" if ok else "FAIL"
        return f'<span class="badge {cls}">{text}</span>'

    file_rows = "\n".join(f"<tr><td>{html.escape(f)}</td></tr>" for f in info["files"])
    if not file_rows:
        file_rows = "<tr><td>(none)</td></tr>"

    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>{html.escape(title)} - Dev Verify</title>
<style>
  :root {{ --bg: #0f172a; --card: #1e293b; --text: #e2e8f0; --muted: #94a3b8; --accent: #3b82f6; }}
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ font-family: -apple-system, 'Segoe UI', sans-serif; background: var(--bg); color: var(--text); padding: 2rem; line-height: 1.6; }}
  .container {{ max-width: 900px; margin: 0 auto; }}
  h1 {{ font-size: 1.5rem; margin-bottom: 1rem; color: #fff; }}
  .meta {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.75rem; margin-bottom: 1.5rem; }}
  .meta-item {{ background: var(--card); border-radius: 8px; padding: 0.75rem 1rem; }}
  .meta-item .label {{ font-size: 0.75rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.05em; }}
  .meta-item .value {{ font-size: 1rem; font-weight: 600; margin-top: 0.25rem; }}
  .badge {{ display: inline-block; padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.85rem; font-weight: 600; color: #fff; }}
  .badge-pass {{ background: #16a34a; }}
  .badge-fail {{ background: #dc2626; }}
  .section {{ background: var(--card); border-radius: 8px; padding: 1rem 1.25rem; margin-bottom: 1rem; }}
  .section h2 {{ font-size: 1rem; color: var(--accent); margin-bottom: 0.75rem; }}
  .section table {{ width: 100%; border-collapse: collapse; font-size: 0.9rem; }}
  .section td {{ padding: 0.3rem 0.5rem; border-bottom: 1px solid #334155; }}
  .checklist {{ list-style: none; }}
  .checklist li {{ padding: 0.4rem 0; display: flex; align-items: center; gap: 0.5rem; }}
  .checklist input[type="checkbox"] {{ width: 18px; height: 18px; accent-color: var(--accent); }}
  pre {{ background: #0f172a; border: 1px solid #334155; border-radius: 6px; padding: 1rem; overflow-x: auto; font-size: 0.8rem; line-height: 1.5; max-height: 400px; overflow-y: auto; white-space: pre-wrap; word-break: break-all; }}
  .footer {{ margin-top: 2rem; text-align: center; color: var(--muted); font-size: 0.8rem; }}
</style>
</head>
<body>
<div class="container">
  <h1>{html.escape(title)}</h1>
  <div class="meta">
    <div class="meta-item"><div class="label">Commit</div><div class="value">{info['hash']}</div></div>
    <div class="meta-item"><div class="label">Author</div><div class="value">{html.escape(info['author'])}</div></div>
    <div class="meta-item"><div class="label">Commit Time</div><div class="value">{info['date']}</div></div>
    <div class="meta-item"><div class="label">Verify Time</div><div class="value">{verify_time}</div></div>
    <div class="meta-item"><div class="label">Build</div><div class="value">{badge(build_ok)}</div></div>
    <div class="meta-item"><div class="label">Test</div><div class="value">{badge(test_ok)}</div></div>
    <div class="meta-item"><div class="label">Diff Check</div><div class="value">{badge(diff_ok)}</div></div>
    <div class="meta-item"><div class="label">Overall</div><div class="value">{badge(all_passed)}</div></div>
  </div>

  <div class="section">
    <h2>Changed Files</h2>
    <table><tbody>
{file_rows}
    </tbody></table>
  </div>

  <div class="section">
    <h2>Verification Checklist</h2>
    <ul class="checklist">
      <li><input type="checkbox" id="c1"><label for="c1">Core functionality works</label></li>
      <li><input type="checkbox" id="c2"><label for="c2">Edge cases handled</label></li>
      <li><input type="checkbox" id="c3"><label for="c3">No regressions</label></li>
      <li><input type="checkbox" id="c4"><label for="c4">Ready to push</label></li>
    </ul>
  </div>

  <div class="section">
    <h2>Build Output</h2>
    <pre>{html.escape(build_out)}</pre>
  </div>

  <div class="section">
    <h2>Test Output</h2>
    <pre>{html.escape(test_out)}</pre>
  </div>

  <div class="section">
    <h2>Diff Check</h2>
    <pre>{html.escape(diff_out)}</pre>
  </div>

  <div class="footer">dev_verify.py &middot; {verify_time}</div>
</div>
</body>
</html>
"""


def main():
    parser = argparse.ArgumentParser(description="Dev verification workflow")
    parser.add_argument("-c", "--commit", default="HEAD", help="Commit hash (default: HEAD)")
    parser.add_argument("-t", "--title", default="", help="Record title (default: commit message)")
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    log_dir = root / "dev-log"
    log_dir.mkdir(exist_ok=True)

    # 1. Collect commit info
    info = collect_commit_info(root, args.commit)
    title = args.title or info["message"]

    print("=== DEV VERIFY ===", flush=True)
    print(f"Commit: {info['hash']} - {info['message']}", flush=True)
    print(f"Author: {info['author']} ({info['date']})", flush=True)
    print(flush=True)

    # 2-4. Run checks
    build_ok, build_out = build(root)
    test_ok, test_out = test(root)
    diff_ok, diff_out = diff_check(root)

    # 5. Generate records
    verify_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    slug = re.sub(r"[^\w\u4e00-\u9fff-]", "", title)
    slug = re.sub(r"-{2,}", "-", slug)[:40]
    base_name = f"{timestamp}_{slug}"
    md_path = log_dir / f"{base_name}.md"
    html_path = log_dir / f"{base_name}.html"

    md_content = generate_markdown(
        title, info, build_ok, test_ok, diff_ok, build_out, test_out, diff_out, verify_time
    )
    html_content = generate_html(
        title, info, build_ok, test_ok, diff_ok, build_out, test_out, diff_out, verify_time
    )

    md_path.write_text(md_content, encoding="utf-8")
    html_path.write_text(html_content, encoding="utf-8")

    print(flush=True)
    print("-- RECORDS --", flush=True)
    print(f"  MD:   {md_path}", flush=True)
    print(f"  HTML: {html_path}", flush=True)

    # Summary
    all_passed = build_ok and test_ok and diff_ok
    print(flush=True)
    print("=== RESULT ===", flush=True)
    if all_passed:
        print("  ALL PASSED - ready to push", flush=True)
    else:
        print("  ISSUES FOUND - check output", flush=True)
    print(flush=True)
    print(f'  Open HTML: start "{html_path}"', flush=True)

    sys.exit(0 if all_passed else 1)


if __name__ == "__main__":
    main()
