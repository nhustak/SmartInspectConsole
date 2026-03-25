# AGENTS.md instructions for C:\Project\SmartInspectConsole

## Global AGENTS Source
- Read `C:\Project\_instructions\AGENTS.md` first at the start of every task.
- Treat `C:\Project\_instructions\AGENTS.md` as the baseline global policy.
- Apply local rules in this file as repo-specific additions/overrides.

## Project directories
- C:\Project\SmartInspectConsole

## Local workflow
- For any change in this repository, always increment the application version by one revision (`x.y.z.n` -> `x.y.z.(n+1)`) before responding.
- If a change directly affects the application UI, runtime behavior, listener behavior, commands, packaging, or any shipped app output, always shut down any currently running `SmartInspectConsole` process, build, and start the updated app before responding.
- If a change does not directly affect the shipped app, do not stop, build, or restart the app unless the user explicitly asks for it.
- Do not wait for a separate reminder. Treat this as the default development cycle for this repository unless the user explicitly says otherwise.
