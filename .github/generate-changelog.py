import json, subprocess, os, sys, urllib.request, urllib.error

tmpdir = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("CHANGELOG_TMPDIR", ".")

script_dir = os.path.dirname(os.path.abspath(__file__))
template_path = os.path.join(script_dir, "changelog-prompt.txt")

with open(template_path, "r", encoding="utf-8") as f:
    prompt = f.read()

def read_file(name):
    path = os.path.join(tmpdir, name)
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            return f.read()
    return ""

prompt = prompt.replace("{{COMMIT_LOG}}", read_file("commit_log.txt"))
prompt = prompt.replace("{{CODE_DIFF}}", read_file("code_diff.txt"))
prompt = prompt.replace("{{CODE_DIFF_DETAIL}}", read_file("code_diff_detail.txt"))

api_key = os.environ["DEEPSEEK_API_KEY"]

payload = json.dumps({
    "model": "deepseek-v4-pro",
    "messages": [
        {"role": "system", "content": "你是一位资深的技术文档工程师，精通 .NET/WinUI 开发，擅长阅读代码 diff 并将其转化为用户友好的更新说明。你输出的更新日志将直接用于 GitHub Release 页面，面向普通用户。输出纯 Markdown 格式，不要用代码块包裹整体内容。"},
        {"role": "user", "content": prompt}
    ],
    "temperature": 0.3,
    # 推理型模型会先消耗 token 再产出正文，4000 在长 diff 下可能只剩空 content。
    "max_tokens": 8000
}).encode("utf-8")

req = urllib.request.Request(
    "https://api.deepseek.com/chat/completions",
    data=payload,
    headers={
        "Content-Type": "application/json",
        "Authorization": f"Bearer {api_key}"
    }
)

try:
    with urllib.request.urlopen(req, timeout=300) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    content = (data["choices"][0]["message"]["content"] or "").strip()
    if not content:
        raise ValueError("模型返回了空内容（可能 token 全被推理占用）")
except Exception as e:
    commit_log = read_file("commit_log.txt").strip()
    if commit_log and commit_log != "- 首次发布":
        content = "## 更新内容\n\n" + commit_log + f"\n\n> 注：AI 摘要生成失败（{e}），以上为本次变更的原始提交记录。"
    else:
        content = f"AI 生成更新日志失败: {e}\n请查看 git 历史获取详细变更信息。"

with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
    f.write("result<<EOF\n")
    f.write(content + "\n")
    f.write("EOF\n")
