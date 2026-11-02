# Security policy

## How to report a vulnerability

**Do not open a public issue.** Use GitHub's private advisory: **Security** tab → **Report a vulnerability**.
Include the version (or commit), the operating system, the steps to reproduce it and its effect. We will reply as soon as possible
and work with you on the fix and its disclosure.

Only the latest published version and the `development` branch are supported.

## What we consider a vulnerability

AgentTrace Studio is a local, **offline** application: it opens no network connections and sends no telemetry. The problems we
care about are, for example:

- the application **writes, modifies or deletes** files of the observed project (it must only read them);
- it reads or shows files **outside** the observed project, for example by following symbolic links;
- a crafted trace, file name or repository causes code execution, an unexpected crash or excessive resource
  consumption;
- any outgoing network connection.

## Trace privacy

By design, traces are shown **as they are**, without hiding secrets (keys, passwords, personal paths).
For that reason, **do not paste real traces into issues or pull requests** without reviewing them: they may contain credentials. To
reproduce a problem, use a synthetic trace or reduce the real one and clean it first.
