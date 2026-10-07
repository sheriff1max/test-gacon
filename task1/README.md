# WukongBench — автозапуск Black Myth: Wukong Benchmark Tool

Один файл кода Program.cs. Запускает бенчмарк два раза (CPU и GPU), сам применяет настройки, дожидается завершения и собирает отчёт (консоль + wukong_benchmark_report.md).

## Запуск

Что нужно для запуска: Windows, .NET 10.0, Steam + установленный Benchmark Tool

1. `dotnet run --project WukongBench.csproj -- calibrate` - дождитесь меню, курсор на START TEST, Enter. Убедитесь, что вывелось button.json: x=… y=….
2. `dotnet run --project WukongBench.csproj` - cpu/gpu проходы и отчёт в консоль.