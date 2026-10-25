"""NES game benchmark for small local models (Ollama) using WinASM65's MCP server.

Each task (tasks/<id>.json) asks for a change to the NROM project template
(knowledge/nes/templates/nrom-game). The model works in a copy of it, through an
agent loop: Ollama's chat API with tools = every tool of `WinASM65.Tooling --mcp`
plus four file tools (list_files, read_file, write_file, replace_in_file), the
built-in tools an editor agent such as Continue gives it. Then the project is
built and the task's check (tasks/<id>.lua, after checks/lib.lua) runs the ROM in
Mesen2's test runner, presses buttons and asserts what the hardware shows.

Usage (from the repository root):

    # validate the checks: each reference solution passes, the bare template fails
    python tools/nes-ai-bench/run-bench.py --solutions

    # run models on tasks (sequentially, one model loaded at a time)
    python tools/nes-ai-bench/run-bench.py --models qwen3:4b,qwen2.5-coder:3b --tasks 01,02,03,04

Options: --steps N (tool calls per task, default 25), --minutes M (wall clock per
task, default 15), --num-ctx N (default 8192), --tooling PATH (WinASM65.Tooling.exe),
--mesen PATH (Mesen.exe), --ollama URL, --strict-tools (do not accept tool calls
written as JSON text), --kill-mesen (close a running Mesen2 first: it is single-instance),
--tool-set all|core (core = file tools, build_project, get_listing, find_symbol,
assemble and the nes_* tools: 1,245 tokens of schemas instead of 3,776).

An agent that repeats the very same call is told so on the 3rd time and stopped on
the 5th. For qwen3 models the system prompt starts with /no_think and <think>
blocks are removed from replies.

Outputs: results/<model>.md (table: task, result, steps, seconds, reason), and the
transcripts in work/<model>/<task>/transcript.json (work/ is not versioned).
"""
import argparse
import datetime
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
TEMPLATE = os.path.join(REPO, "knowledge", "nes", "templates", "nrom-game")
TASKS = os.path.join(HERE, "tasks")
SOLUTIONS = os.path.join(HERE, "solutions")
WORK = os.path.join(HERE, "work")
RESULTS = os.path.join(HERE, "results")
LIB_LUA = os.path.join(HERE, "checks", "lib.lua")
BUILD_OUTPUTS = (".o", ".symb", ".lst", ".nes", ".Unsolved", ".UnsolvedExpr")
TOOL_RESULT_LIMIT = 4000  # characters of one tool result kept in the conversation


# ---------------------------------------------------------------------------- files and projects

def copy_template(target):
    if os.path.exists(target):
        shutil.rmtree(target)
    shutil.copytree(TEMPLATE, target, ignore=lambda d, names: [n for n in names if n.endswith(BUILD_OUTPUTS)])


def overlay(source, target):
    for root, _, files in os.walk(source):
        for name in files:
            rel = os.path.relpath(os.path.join(root, name), source)
            os.makedirs(os.path.dirname(os.path.join(target, rel)), exist_ok=True)
            shutil.copy(os.path.join(root, name), os.path.join(target, rel))


def load_tasks(selection):
    tasks = []
    for name in sorted(os.listdir(TASKS)):
        if name.endswith(".json"):
            with open(os.path.join(TASKS, name), encoding="utf-8") as f:
                tasks.append(json.load(f))
    if selection:
        wanted = [s.strip() for s in selection.split(",") if s.strip()]
        tasks = [t for t in tasks if any(t["id"] == w or t["id"].startswith(w) for w in wanted)]
    return tasks


def find_tooling(given):
    if given:
        return given
    for config in ("Release", "Debug"):
        path = os.path.join(REPO, "WinASM65.Tooling", "bin", config, "net8.0", "WinASM65.Tooling.exe")
        if os.path.exists(path):
            return path
    sys.exit("WinASM65.Tooling.exe not found: dotnet build WinASM65.Tooling -c Release, or pass --tooling")


# ---------------------------------------------------------------------------- build and check

def build(tooling, folder):
    """Builds with `WinASM65.Tooling --build`; returns (ok, message)."""
    try:
        run = subprocess.run([tooling, "--build", os.path.join(folder, "config.json")], capture_output=True, text=True,
                             timeout=120, cwd=folder)
    except subprocess.TimeoutExpired:
        return False, "build timed out"
    out = (run.stdout + run.stderr).strip()
    if run.returncode != 0:
        errors = [l for l in out.splitlines() if ": error: " in l]
        first = errors[0] if errors else (out.splitlines()[-1] if out else "build failed")
        return False, "build failed: " + first.replace(folder + os.sep, "")
    return True, out.splitlines()[-1] if out else "built"


def mesen_pids(mesen):
    """Processes running this Mesen.exe (by path: a Mesen elsewhere, e.g. the user's, is left alone)."""
    command = ("Get-CimInstance Win32_Process -Filter \"Name='Mesen.exe'\" | ForEach-Object { \"$($_.ProcessId)|$($_.ExecutablePath)\" }")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, timeout=60).stdout
    except (OSError, subprocess.TimeoutExpired):
        return []
    target = os.path.normcase(os.path.abspath(mesen))
    pids = []
    for line in out.splitlines():
        pid, _, path = line.partition("|")
        if path and os.path.normcase(os.path.abspath(path.strip())) == target:
            pids.append(pid.strip())
    return pids


def single_instance(mesen):
    """Mesen2's SingleInstance preference (settings.json next to the exe); true when unknown."""
    try:
        with open(os.path.join(os.path.dirname(mesen), "settings.json"), encoding="utf-8") as f:
            return not re.search(r'"SingleInstance"\s*:\s*false', f.read())
    except OSError:
        return True


def other_mesen_running(mesen):
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Mesen.exe", "/NH"], capture_output=True, text=True).stdout
    except OSError:
        return False
    return out.count("Mesen.exe") > len(mesen_pids(mesen))


def kill_mesen(mesen):
    for pid in mesen_pids(mesen):
        subprocess.run(["taskkill", "/F", "/PID", pid], capture_output=True)


def run_check(mesen, task, folder):
    """Runs the task's Lua check on the built ROM; returns (passed, message)."""
    roms = [f for f in os.listdir(folder) if f.endswith(".nes")]
    if not roms:
        return False, "no .nes file was built"
    rom = os.path.join(folder, roms[0])
    result = os.path.join(folder, "check-result.txt")
    if os.path.exists(result):
        os.remove(result)
    with open(LIB_LUA, encoding="utf-8") as f:
        lib = f.read().replace("__RESULT__", result.replace("\\", "/"))
    with open(os.path.join(TASKS, task["check"]), encoding="utf-8") as f:
        check = f.read()
    script = os.path.join(folder, "check.lua")
    with open(script, "w", encoding="utf-8") as f:
        f.write(lib + "\n" + check)
    try:
        subprocess.run([mesen, "--testRunner", script, rom], capture_output=True, timeout=120)
    except subprocess.TimeoutExpired:
        kill_mesen(mesen)
        return False, "check timed out (Mesen2 killed)"
    if not os.path.exists(result):
        return False, "the check wrote no verdict (Lua error or emulator crash)"
    with open(result, encoding="utf-8") as f:
        verdict = f.read().strip()
    return verdict.startswith("PASS"), verdict[5:] if len(verdict) > 5 else verdict


# ---------------------------------------------------------------------------- MCP client

class McpClient:
    """WinASM65.Tooling --mcp over stdio, one JSON message per line."""

    def __init__(self, tooling, cwd):
        self.proc = subprocess.Popen([tooling, "--mcp"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.DEVNULL, cwd=cwd)
        self.next_id = 0
        init = self.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                           "clientInfo": {"name": "nes-ai-bench", "version": "1"}})
        self.instructions = init.get("result", {}).get("instructions", "")
        self.proc.stdin.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
        self.proc.stdin.flush()
        self.tools = self.request("tools/list")["result"]["tools"]

    def request(self, method, params=None):
        self.next_id += 1
        message = {"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}}
        self.proc.stdin.write((json.dumps(message) + "\n").encode("utf-8"))
        self.proc.stdin.flush()
        while True:
            line = self.proc.stdout.readline()
            if not line:
                raise RuntimeError("the MCP server exited")
            reply = json.loads(line)
            if reply.get("id") == self.next_id:
                return reply

    def call(self, name, arguments):
        reply = self.request("tools/call", {"name": name, "arguments": arguments})
        if "error" in reply:
            return reply["error"].get("message", "error"), True
        result = reply["result"]
        parts = []
        for c in result.get("content", []):
            parts.append(c.get("text", "") if c.get("type") == "text" else "[%s omitted]" % c.get("type"))
        return "\n".join(parts), bool(result.get("isError"))

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=10)
        except Exception:
            self.proc.kill()


# ---------------------------------------------------------------------------- file tools (the editor's)

FILE_TOOLS = [
    {"name": "list_files", "description": "Lists the files of the project folder (or of a subfolder).",
     "inputSchema": {"type": "object", "properties": {"path": {"type": "string", "description": "Subfolder (default: the project)"}}}},
    {"name": "read_file", "description": "Returns the text of a project file.",
     "inputSchema": {"type": "object", "properties": {"path": {"type": "string", "description": "File path, e.g. src/game.asm"}},
                     "required": ["path"]}},
    {"name": "write_file", "description": "Creates or replaces a project file with the given text (the whole file).",
     "inputSchema": {"type": "object", "properties": {"path": {"type": "string", "description": "File path, e.g. src/game.asm"},
                                                      "content": {"type": "string", "description": "The whole new text"}},
                     "required": ["path", "content"]}},
    {"name": "replace_in_file", "description": "Replaces an exact piece of text in a project file by new text. "
                                               "old must appear exactly once (copy it from read_file without the line numbers).",
     "inputSchema": {"type": "object", "properties": {"path": {"type": "string", "description": "File path, e.g. src/game.asm"},
                                                      "old": {"type": "string", "description": "Exact text to replace"},
                                                      "new": {"type": "string", "description": "Replacement text"}},
                     "required": ["path", "old", "new"]}},
]


class FileTools:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self.writes = 0

    def path(self, rel):
        full = os.path.abspath(os.path.join(self.root, rel or "."))
        if not (full == self.root or full.startswith(self.root + os.sep)):
            raise ValueError("path outside the project folder: " + str(rel))
        return full

    def call(self, name, args):
        try:
            if name == "list_files":
                base = self.path(args.get("path"))
                found = []
                for r, _, files in os.walk(base):
                    for f in files:
                        if not f.endswith(BUILD_OUTPUTS) and not f.startswith("check"):
                            found.append(os.path.relpath(os.path.join(r, f), self.root).replace("\\", "/"))
                return "\n".join(sorted(found)) or "(empty)", False
            if name == "read_file":
                # Plain text, as Continue's read tool returns it: small models copy line numbers into their edits.
                with open(self.path(args["path"]), encoding="utf-8", errors="replace") as f:
                    return f.read(), False
            if name == "write_file":
                full = self.path(args["path"])
                os.makedirs(os.path.dirname(full), exist_ok=True)
                content = args["content"]
                lines = content.split("\n")
                if sum(1 for l in lines if re.match(r"^\s*\d+(  |\t)", l)) > 0.8 * len(lines):
                    content = "\n".join(re.sub(r"^\s*\d+(  |\t)", "", l) for l in lines)  # pasted line numbers
                with open(full, "w", encoding="utf-8", newline="\n") as f:
                    f.write(content)
                self.writes += 1
                return "Wrote %s (%d lines)." % (args["path"], content.count("\n") + 1), False
            if name == "replace_in_file":
                full = self.path(args["path"])
                with open(full, encoding="utf-8") as f:
                    text = f.read()
                old = args["old"]
                count = text.count(old)
                if count == 0:
                    # Models often keep the line numbers of read_file or change indentation.
                    stripped = "\n".join(re.sub(r"^\s*\d+(  |\t)", "", l) for l in old.split("\n"))
                    if stripped != old and text.count(stripped) == 1:
                        old, count = stripped, 1
                if count != 1:
                    return ("old text found %d times in %s; it must appear exactly once. Read the file and copy the "
                            "exact lines." % (count, args["path"])), True
                with open(full, "w", encoding="utf-8", newline="\n") as f:
                    f.write(text.replace(old, args["new"]))
                self.writes += 1
                return "Replaced in %s." % args["path"], False
        except KeyError as e:
            return "missing argument %s" % e, True
        except (OSError, ValueError) as e:
            return str(e), True
        return "unknown file tool " + name, True


# ---------------------------------------------------------------------------- Ollama

class Ollama:
    def __init__(self, url):
        self.url = url.rstrip("/")

    def post(self, path, body, timeout):
        request = urllib.request.Request(self.url + path, data=json.dumps(body).encode("utf-8"),
                                         headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))

    def loaded(self):
        with urllib.request.urlopen(self.url + "/api/ps", timeout=30) as response:
            return [m["name"] for m in json.loads(response.read().decode("utf-8")).get("models", [])]

    def unload(self, model):
        try:
            self.post("/api/generate", {"model": model, "keep_alive": 0}, 120)
        except (urllib.error.URLError, OSError):
            pass

    def only(self, model):
        """Unloads every other model, so one model at a time uses the GPU."""
        for name in self.loaded():
            if name != model:
                self.unload(name)


THINK = re.compile(r"<think>.*?(</think>|$)", re.S)


def parse_text_tool_calls(content, names):
    """Tool calls a model wrote as text: {"name": ..., "arguments": {...}} objects, bare or in <tool_call> tags."""
    calls = []
    decoder = json.JSONDecoder()
    i = 0
    while i < len(content):
        start = content.find("{", i)
        if start < 0:
            break
        try:
            obj, end = decoder.raw_decode(content, start)
        except ValueError:
            i = start + 1
            continue
        i = end
        if isinstance(obj, dict) and obj.get("name") in names:
            args = obj.get("arguments", obj.get("parameters", {}))
            if isinstance(args, str):
                try:
                    args = json.loads(args)
                except ValueError:
                    args = {}
            calls.append({"function": {"name": obj["name"], "arguments": args if isinstance(args, dict) else {}}})
    return calls


def system_prompt(model, folder, instructions):
    config = os.path.join(folder, "config.json")
    text = (
        "You are an expert NES programmer. You work in a WinASM65 project folder: %s\n"
        "The project file is %s . The game code is src/game.asm.\n"
        "Tools: list_files, read_file, write_file, replace_in_file edit the project files (paths relative to the "
        "project folder). nes_reference, nes_search and nes_snippet give NES hardware facts and tested WinASM65 "
        "code: look facts up instead of guessing. build_project builds the ROM.\n"
        "Method:\n"
        "1. read_file src/game.asm first.\n"
        "2. Look up what you need with nes_snippet or nes_reference.\n"
        "3. Change the code with replace_in_file (exact old text) or write_file (whole file).\n"
        "4. Call build_project with config \"%s\" after every change, and fix the errors it reports.\n"
        "5. When the build succeeds and the task is done, reply with a one-line summary and no tool call.\n"
        "Server notes: %s" % (folder, config, config, instructions))
    if model.startswith("qwen3"):
        text = "/no_think\n" + text
    return text


def estimate_tokens(messages, tools_chars):
    # Measured with qwen2.5-coder:3b: the 36 tool schemas (17,191 characters) are 3,776 tokens, 4.5 characters a
    # token; code and hex dumps are denser, so count 3.5 for the messages.
    return (sum(len(m.get("content") or "") + len(json.dumps(m.get("tool_calls", []))) for m in messages)) * 2 // 7 \
        + tools_chars * 2 // 9


CORE_TOOLS = {"list_files", "read_file", "write_file", "replace_in_file", "build_project", "get_listing", "find_symbol",
              "assemble", "nes_reference", "nes_search", "nes_snippet"}
REPEAT_REFUSE = 3   # the 3rd identical call in a row is not run: the model is told to do something else
REPEAT_STOP = 5     # the 5th ends the task


def run_agent(args, ollama, model, task, folder, tooling):
    """The agent loop. Returns a dict with steps, reasons and counters."""
    mcp = McpClient(tooling, folder)
    files = FileTools(folder)
    try:
        catalog = FILE_TOOLS + mcp.tools
        if args.tool_set == "core":
            catalog = [t for t in catalog if t["name"] in CORE_TOOLS]
        names = {t["name"] for t in catalog}
        ollama_tools = [{"type": "function", "function": {"name": t["name"], "description": t.get("description", ""),
                                                          "parameters": t.get("inputSchema", {"type": "object", "properties": {}})}}
                        for t in catalog]
        tools_chars = len(json.dumps(ollama_tools))
        messages = [{"role": "system", "content": system_prompt(model, folder, mcp.instructions)},
                    {"role": "user", "content": task["prompt"]}]
        info = {"steps": 0, "bad_calls": 0, "text_calls": 0, "nudges": 0, "tools_used": {}, "end": "",
                "prompt_tokens_max": 0, "transcript": messages}
        last_call, repeats = None, 0
        started = time.time()
        while True:
            if info["steps"] >= args.steps:
                info["end"] = "step budget (%d tool calls) used up" % args.steps
                break
            if time.time() - started > args.minutes * 60:
                info["end"] = "time budget (%d min) used up" % args.minutes
                break
            # Keep the conversation within the context: older tool results go first.
            budget = int(args.num_ctx * 0.8)
            tool_indexes = [i for i, m in enumerate(messages) if m["role"] == "tool"]
            for i in tool_indexes[:-2]:
                if estimate_tokens(messages, tools_chars) <= budget:
                    break
                if not messages[i]["content"].startswith("[older result removed"):
                    messages[i]["content"] = "[older result removed to save context]"
            body = {"model": model, "messages": messages, "tools": ollama_tools, "stream": False,
                    "keep_alive": "10m", "options": {"num_ctx": args.num_ctx, "temperature": 0.2, "num_predict": args.num_predict}}
            if model.startswith("qwen3"):
                body["think"] = False
            try:
                reply = ollama.post("/api/chat", body, 600)
            except urllib.error.HTTPError as e:
                info["end"] = "ollama HTTP %d: %s" % (e.code, e.read().decode("utf-8", "replace")[:200])
                break
            except (urllib.error.URLError, OSError) as e:
                info["end"] = "ollama error: %s" % e
                break
            info["prompt_tokens_max"] = max(info["prompt_tokens_max"], reply.get("prompt_eval_count", 0) or 0)
            message = reply.get("message", {})
            content = THINK.sub("", message.get("content") or "").strip()
            calls = message.get("tool_calls") or []
            if not calls and not args.strict_tools:
                calls = parse_text_tool_calls(content, names)
                info["text_calls"] += len(calls)
            messages.append({"role": "assistant", "content": content, "tool_calls": calls} if calls
                            else {"role": "assistant", "content": content})
            if not calls:
                if files.writes == 0 and info["nudges"] == 0:
                    info["nudges"] += 1
                    messages.append({"role": "user", "content": "You did not change any file. Use the tools: read_file, "
                                     "then replace_in_file or write_file, then build_project."})
                    continue
                info["end"] = "model finished" if files.writes else "answered without tool calls (no file changed)"
                break
            for call in calls:
                function = call.get("function", {})
                name = function.get("name", "")
                arguments = function.get("arguments", {})
                if isinstance(arguments, str):
                    try:
                        arguments = json.loads(arguments)
                    except ValueError:
                        arguments = None
                info["steps"] += 1
                info["tools_used"][name] = info["tools_used"].get(name, 0) + 1
                signature = name + json.dumps(arguments, sort_keys=True)
                repeats = repeats + 1 if signature == last_call else 1
                last_call = signature
                if repeats >= REPEAT_STOP:
                    info["end"] = "stuck repeating the same %s call" % name
                    break
                if repeats >= REPEAT_REFUSE:
                    text, error = ("you already made this exact call %d times and got the same answer; do something "
                                   "different (read the error, look it up, change the code)" % (repeats - 1)), True
                elif not isinstance(arguments, dict):
                    text, error = "the arguments are not a JSON object", True
                elif name not in names:
                    text, error = "unknown tool %r. Tools: %s" % (name, ", ".join(sorted(names))), True
                elif name in ("list_files", "read_file", "write_file", "replace_in_file"):
                    text, error = files.call(name, arguments)
                else:
                    if name == "debug_launch":
                        arguments["emulatorPath"] = args.mesen  # the bench's Mesen, never another running instance
                    try:
                        text, error = mcp.call(name, arguments)
                    except (RuntimeError, ValueError, OSError) as e:
                        text, error = "tool failed: %s" % e, True
                if error:
                    info["bad_calls"] += 1
                if len(text) > TOOL_RESULT_LIMIT:
                    text = text[:TOOL_RESULT_LIMIT] + "\n[... cut: %d more characters]" % (len(text) - TOOL_RESULT_LIMIT)
                messages.append({"role": "tool", "tool_name": name, "content": ("ERROR: " if error else "") + text})
                if info["steps"] >= args.steps:
                    break
            if info["end"].startswith("stuck"):
                break
        info["seconds"] = time.time() - started
        info["writes"] = files.writes
        return info
    finally:
        mcp.close()


# ---------------------------------------------------------------------------- modes

def safe(name):
    return re.sub(r"[^A-Za-z0-9._-]+", "_", name)


def run_solutions(args, tooling):
    tasks = load_tasks(args.tasks)
    failures = 0
    rows = []
    for task in tasks:
        folder = os.path.join(WORK, "solutions", task["id"])
        copy_template(folder)
        overlay(os.path.join(SOLUTIONS, task["id"]), folder)
        ok, message = build(tooling, folder)
        passed, verdict = run_check(args.mesen, task, folder) if ok else (False, message)
        # The bare template must FAIL the check, or the check proves nothing.
        bare = os.path.join(WORK, "solutions", task["id"] + "-template")
        copy_template(bare)
        build(tooling, bare)
        bare_passed, bare_verdict = run_check(args.mesen, task, bare)
        good = passed and not bare_passed
        failures += not good
        rows.append((task["id"], "PASS" if passed else "FAIL", verdict, "fails (good)" if not bare_passed else "PASSES (bad check)", bare_verdict))
        print("%-20s solution %s: %s | template %s: %s" % rows[-1], flush=True)
    os.makedirs(RESULTS, exist_ok=True)
    with open(os.path.join(RESULTS, "solutions.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# Reference solutions\n\nRun %s. Each check must pass on its solution and fail on the bare template.\n\n"
                % datetime.datetime.now().strftime("%Y-%m-%d %H:%M"))
        f.write("| Task | Solution | Check says | Template | Check says |\n|---|---|---|---|---|\n")
        for r in rows:
            f.write("| %s | %s | %s | %s | %s |\n" % r)
    print("%d task(s), %d problem(s)" % (len(tasks), failures))
    return 1 if failures else 0


def run_models(args, tooling):
    ollama = Ollama(args.ollama)
    tasks = load_tasks(args.tasks)
    for model in [m.strip() for m in args.models.split(",") if m.strip()]:
        ollama.only(model)
        rows = []
        for task in tasks:
            folder = os.path.join(WORK, safe(model) + ("" if args.tool_set == "all" else "-" + args.tool_set), task["id"])
            copy_template(folder)
            print("[%s] %s ..." % (model, task["id"]), flush=True)
            info = run_agent(args, ollama, model, task, folder, tooling)
            if mesen_pids(args.mesen):  # started by the model's debug_launch; none was running when the bench began
                kill_mesen(args.mesen)
                time.sleep(2)
            ok, message = build(tooling, folder)
            if ok:
                passed, reason = run_check(args.mesen, task, folder)
            else:
                passed, reason = False, message
            if not passed and info["end"] not in ("model finished",):
                reason = info["end"] + "; " + reason
            rows.append({"task": task["id"], "level": task.get("level", ""), "passed": passed, "steps": info["steps"],
                         "seconds": info["seconds"], "reason": reason, "info": info})
            with open(os.path.join(folder, "transcript.json"), "w", encoding="utf-8") as f:
                json.dump({k: v for k, v in info.items()}, f, indent=1)
            print("   %s in %d steps, %.0f s: %s" % ("PASS" if passed else "FAIL", info["steps"], info["seconds"], reason), flush=True)
        ollama.unload(model)
        write_results(model, rows, args)


def write_results(model, rows, args):
    os.makedirs(RESULTS, exist_ok=True)
    path = os.path.join(RESULTS, safe(model) + ("" if args.tool_set == "all" else "-" + args.tool_set) + ".md")
    passed = sum(1 for r in rows if r["passed"])
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# %s\n\n" % model)
        f.write("Run %s: %d/%d passed. Budget %d tool calls, %d min per task, num_ctx %d, tool set %s, tool calls "
                "in text %s.\n\n" % (datetime.datetime.now().strftime("%Y-%m-%d %H:%M"), passed, len(rows), args.steps,
                                    args.minutes, args.num_ctx, args.tool_set, "refused" if args.strict_tools else "accepted"))
        f.write("| Task | Result | Steps | Seconds | Reason |\n|---|---|---|---|---|\n")
        for r in rows:
            f.write("| %s | %s | %d | %.0f | %s |\n" % (r["task"], "PASS" if r["passed"] else "FAIL", r["steps"], r["seconds"],
                                                      r["reason"].replace("|", "/").replace("\n", " ")))
        f.write("\n## Details\n\n| Task | Tools used | Failed calls | Calls written as text | Nudged | Files changed | Max prompt tokens | Ended |\n"
                "|---|---|---|---|---|---|---|---|\n")
        for r in rows:
            i = r["info"]
            used = ", ".join("%s x%d" % (k, v) for k, v in sorted(i["tools_used"].items(), key=lambda kv: -kv[1]))
            f.write("| %s | %s | %d | %d | %s | %d | %d | %s |\n" % (r["task"], used or "-", i["bad_calls"], i["text_calls"],
                                                                 "yes" if i["nudges"] else "no", i["writes"], i["prompt_tokens_max"], i["end"]))
    print("results: " + path)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--solutions", action="store_true", help="check every reference solution (and the bare template)")
    parser.add_argument("--models", default="", help="comma-separated Ollama models")
    parser.add_argument("--tasks", default="", help="comma-separated task ids or prefixes (default: all)")
    parser.add_argument("--steps", type=int, default=25)
    parser.add_argument("--minutes", type=float, default=15)
    parser.add_argument("--num-ctx", type=int, default=8192)
    parser.add_argument("--num-predict", type=int, default=3072,
                        help="cap on the tokens of one reply (a whole game.asm is ~2,500 tokens)")
    parser.add_argument("--tooling", default=None)
    parser.add_argument("--mesen", default=os.environ.get("MESEN", r"C:\Projects\NES_PROJECTS\Mesen2\Mesen.exe"))
    parser.add_argument("--ollama", default="http://localhost:11434")
    parser.add_argument("--strict-tools", action="store_true", help="refuse tool calls written as JSON text")
    parser.add_argument("--tool-set", choices=["all", "core"], default="all",
                        help="all = every MCP tool + file tools (default); core = file tools, build, listing, symbols, "
                             "assemble and the nes_* tools")
    parser.add_argument("--kill-mesen", action="store_true", help="close a running Mesen2 first")
    args = parser.parse_args()

    tooling = find_tooling(args.tooling)
    if not os.path.exists(args.mesen):
        sys.exit("Mesen2 not found at %s (--mesen)" % args.mesen)
    if mesen_pids(args.mesen):
        if not args.kill_mesen:
            sys.exit("This Mesen2 (%s) is running: close it, or pass --kill-mesen." % args.mesen)
        kill_mesen(args.mesen)
    if single_instance(args.mesen) and other_mesen_running(args.mesen):
        sys.exit("Another Mesen2 is running and %s is single-instance: its test runs would go to that window. Close it, "
                 "or use a portable copy of Mesen2 (Mesen.exe, MesenCore.dll, the two lib*.dll, settings.json with "
                 "\"SingleInstance\": false) and pass --mesen." % args.mesen)
    if args.solutions:
        sys.exit(run_solutions(args, tooling))
    if not args.models:
        sys.exit("give --models or --solutions")
    run_models(args, tooling)


if __name__ == "__main__":
    main()
