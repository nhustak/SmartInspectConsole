# SmartInspect Console Load Tester

The repository now includes a dedicated stress tool at `src/SmartInspectConsole.LoadTester`.

Run TCP load against the desktop listener:

```powershell
dotnet run --project .\src\SmartInspectConsole.LoadTester -- --transport tcp --clients 8 --messages-per-second 2000 --payload-bytes 1024 --duration-seconds 300
```

Run named-pipe load against the pipe listener:

```powershell
dotnet run --project .\src\SmartInspectConsole.LoadTester -- --transport pipe --pipe smartinspect --clients 4 --messages-per-second 1000 --duration-seconds 180
```

Recommended workflow:

1. Start `SmartInspectConsole`.
2. Watch the new diagnostics text in the status bar while the tester is running.
3. Increase `--clients`, `--messages-per-second`, and `--payload-bytes` until the queue depth grows.
4. If you want to simulate the worst case for large payload filtering, use a larger `--payload-bytes` value such as `8192` or `32768`.
