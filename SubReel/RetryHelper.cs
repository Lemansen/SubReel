#nullable enable
using System;
using System.Net.Http;
using System.Threading.Tasks;

public static class RetryHelper
{
    // для методов с результатом
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> action,
        int attempts = 3,
        int delayMs = 1500,
        Action<string>? log = null)
    {
        Exception? lastError = null;

        for (int i = 1; i <= attempts; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is TaskCanceledException)
            {
                lastError = ex;
                log?.Invoke($"Попытка {i}/{attempts}");

                if (i < attempts)
                    await Task.Delay(delayMs);
            }
        }

        throw lastError!;
    }

    // 🔥 ДЛЯ Task без результата
    public static async Task RetryAsync(
        Func<Task> action,
        int attempts = 3,
        int delayMs = 1500,
        Action<string>? log = null)
    {
        Exception? lastError = null;

        for (int i = 1; i <= attempts; i++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (
                ex is HttpRequestException ||
                ex is TaskCanceledException)
            {
                lastError = ex;
                log?.Invoke($"Попытка {i}/{attempts}");

                if (i < attempts)
                    await Task.Delay(delayMs);
            }
        }

        throw lastError!;
    }
}