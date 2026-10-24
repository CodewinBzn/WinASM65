"""Live check of bridge_mesen2.lua against a real Mesen2.

Usage:
    python tools/mesen2-bridge-check.py <Mesen.exe> <rom.nes> <exec-address-hex>

The address must be an instruction the ROM executes repeatedly (an NMI handler
is ideal). Close any running Mesen2 first: it runs as a single instance and
would swallow the launch.

Checks, in order: STATUS while running; a breakpoint stops *before* its
instruction (PUSH STOPPED reason=breakpoint pc=<address>); STEP stops one
instruction later; BREAK REMOVE and BREAK CLEAR really remove; PAUSE stops with
reason=pause; every RESUME is followed by PUSH RESUMED.
"""
import os
import queue
import socket
import subprocess
import sys
import threading
import time

PORT = 45699
HERE = os.path.dirname(os.path.abspath(__file__))
LUA = os.path.join(HERE, "..", "WinASM65.Monitor.Core", "Bridge", "bridge_mesen2.lua")


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    mesen, rom, address = sys.argv[1], sys.argv[2], int(sys.argv[3].lstrip("$"), 16)
    target = "$%04X" % address

    proc = subprocess.Popen([mesen, rom, os.path.abspath(LUA)],
                            env=dict(os.environ, MACHINE_PORT=str(PORT)))
    replies, pushes = queue.Queue(), queue.Queue()
    failures = []

    def check(condition, what):
        print(("PASS " if condition else "FAIL ") + what, flush=True)
        if not condition:
            failures.append(what)

    try:
        deadline = time.time() + 20
        while True:
            try:
                sock = socket.create_connection(("127.0.0.1", PORT), timeout=2)
                break
            except OSError:
                if time.time() > deadline:
                    sys.exit("FAIL the bridge never listened (is another Mesen2 running?)")
                time.sleep(0.25)
        lines = sock.makefile("r", encoding="ascii", newline="\n")

        def reader():
            for line in lines:
                line = line.rstrip("\n")
                (pushes if line.startswith("PUSH") else replies).put(line)
        threading.Thread(target=reader, daemon=True).start()

        def send(command):
            sock.sendall((command + "\n").encode())
            return replies.get(timeout=10)

        def push(prefix, timeout=5):
            end = time.time() + timeout
            while time.time() < end:
                try:
                    line = pushes.get(timeout=max(0.01, end - time.time()))
                except queue.Empty:
                    return None
                if line.startswith(prefix):
                    return line
            return None

        check(send("PING").startswith("OK Mesen2"), "PING answers Mesen2")
        check(send("STATUS") == "OK running", "STATUS reports a running machine")

        send("BREAK SET exec " + target)
        stop = push("PUSH STOPPED")
        check(stop is not None and "reason=breakpoint" in stop and ("pc=" + target) in stop,
              "a breakpoint stops before its instruction: %s" % stop)
        check(send("STATUS").startswith("OK stopped reason=breakpoint"), "STATUS reports the stop")

        send("STEP")
        stop = push("PUSH STOPPED")
        check(stop is not None and "reason=step" in stop and ("pc=" + target) not in stop,
              "STEP stops one instruction later: %s" % stop)

        send("BREAK REMOVE exec " + target)
        send("RESUME")
        check(push("PUSH RESUMED") is not None, "RESUME is followed by PUSH RESUMED")
        check(push("PUSH STOPPED", timeout=2) is None, "a removed breakpoint no longer stops")

        send("BREAK SET exec " + target)
        push("PUSH STOPPED")
        send("BREAK CLEAR")
        send("RESUME")
        push("PUSH RESUMED")
        check(push("PUSH STOPPED", timeout=2) is None, "BREAK CLEAR removes every breakpoint")

        send("PAUSE")
        stop = push("PUSH STOPPED")
        check(stop is not None and "reason=pause" in stop, "PAUSE stops: %s" % stop)
        send("RESUME")
        check(push("PUSH RESUMED") is not None, "the machine runs again")
    finally:
        proc.kill()

    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
