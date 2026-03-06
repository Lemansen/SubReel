using System;
using System.Diagnostics;
using System.IO;

namespace SubReel
{
    public static class UpdaterService
    {
        public static void Run(string newExePath)
        {
            try
            {
                if (!File.Exists(newExePath))
                    return;

                string currentExe = Process.GetCurrentProcess().MainModule!.FileName!;
                string backupExe = currentExe + ".bak";
                string successFlag = currentExe + ".ok";
                string batchPath = Path.Combine(Path.GetTempPath(), "SR_Update.bat");

                string script = $@"
@echo off
timeout /t 2 /nobreak > nul

rem удаляем старый backup если есть
if exist ""{backupExe}"" del /f /q ""{backupExe}""

rem создаем backup текущего exe
move /y ""{currentExe}"" ""{backupExe}"" > nul

rem если backup не создался — выход
if not exist ""{backupExe}"" exit

rem копируем новую версию
copy /y ""{newExePath}"" ""{currentExe}"" > nul

rem если копирование не удалось → rollback
if not exist ""{currentExe}"" (
    move /y ""{backupExe}"" ""{currentExe}"" > nul
    start """" ""{currentExe}""
    exit
)

rem запускаем новую версию
start """" ""{currentExe}"" updated

rem ждем подтверждение запуска
timeout /t 10 /nobreak > nul

rem если подтверждения нет → rollback
if not exist ""{successFlag}"" (
    del /f /q ""{currentExe}"" > nul 2>&1
    move /y ""{backupExe}"" ""{currentExe}"" > nul
    start """" ""{currentExe}""
) else (
    del /f /q ""{backupExe}"" > nul
    del /f /q ""{successFlag}"" > nul
)

del ""{newExePath}""
del ""%~f0""
";

                File.WriteAllText(batchPath, script);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch
            {
                // silent режим — без падений
            }
        }
    }
}