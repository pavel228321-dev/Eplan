using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.Starter;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 23.09.2026: подготовка загрузки сборок EPLAN — общая для Gui и Web.
    /// Живые запуски показали три ловушки:
    /// (1) папка Bin должна быть папкой варианта (где W3u.exe, Electric P8\&lt;версия&gt;\Bin),
    ///     а не Platform\...\Bin — иначе "The Variant ... is not valid";
    /// (2) Eplan.EplApi.Starteru лежит в Platform\&lt;версия&gt;\Bin, .NET сам его не находит —
    ///     нужен запасной AssemblyResolve;
    /// (3) PinToEplan должен отработать ДО JIT-компиляции любого метода, где упомянуты
    ///     типы EPLAN (в т.ч. в catch) — поэтому вызывать PinOnce отдельным шагом.
    /// 24.09.2026: вернули прежнюю схему установки — папку программы копируют в
    /// Platform\&lt;версия&gt;\Bin (к Eplan.EplApi.*u.dll), а в поле «Папка Bin» указывают папку с W3u.exe.
    /// Starteru ищется сначала рядом с exe (и на 1–2 уровня выше, если папку положили подпапкой).
    /// </summary>
    public static class EplanBootstrap
    {
        private static bool _pinned;

        /// <summary>null — папка подходит; иначе — текст ошибки для пользователя.</summary>
        public static string CheckBinPath(string binPath)
        {
            if (string.IsNullOrWhiteSpace(binPath))
                return "Укажите папку Bin вашей EPLAN (где лежит W3u.exe).";
            if (!File.Exists(Path.Combine(binPath, "W3u.exe")))
                return $"В папке «{binPath}» нет W3u.exe. Укажите папку Bin той EPLAN, которую запускаете обычно, " +
                       @"например C:\Program Files\EPLAN\Electric P8\2.9.4\Bin (не Platform\...\Bin).";
            if (FindStarter(binPath) == null)
                return $"Не найдены DLL EPLAN API ({StarterName}.dll). Скопируйте папку программы в Platform\\<версия>\\Bin, " +
                       @"где лежат Eplan.EplApi.*u.dll (например C:\Program Files\EPLAN\Platform\2.9.4\Bin), и запускайте оттуда. " +
                       $"Сейчас программа запущена из «{AppDomain.CurrentDomain.BaseDirectory}».";
            return null;
        }

        public static void PinOnce(string binPath)
        {
            if (_pinned) return;
            RegisterStarterFallback(binPath);
            Pin(binPath);
            _pinned = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Pin(string binPath)
        {
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(binPath);
            resolver.PinToEplan();
        }

        private static void RegisterStarterFallback(string binPath)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = new AssemblyName(args.Name).Name;
                if (!string.Equals(name, StarterName, StringComparison.OrdinalIgnoreCase)) return null;
                string path = FindStarter(binPath);
                return path != null ? Assembly.LoadFrom(path) : null;
            };
        }

        private const string StarterName = "Eplan.EplApi.Starteru";

        private static string FindStarter(string binPath)
        {
            foreach (string dir in StarterSearchDirs(binPath))
            {
                string path = Path.Combine(dir, StarterName + ".dll");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// Папка exe и 2 уровня выше (программа скопирована в Platform\&lt;версия&gt;\Bin или его подпапку),
        /// затем ...\EPLAN\&lt;вариант&gt;\&lt;версия&gt;\Bin -> сама папка и ...\EPLAN\Platform\&lt;версия&gt;\Bin.
        /// </summary>
        private static IEnumerable<string> StarterSearchDirs(string binPath)
        {
            var exeDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 3 && exeDir != null; i++, exeDir = exeDir.Parent)
                yield return exeDir.FullName;

            if (string.IsNullOrWhiteSpace(binPath)) yield break;
            yield return binPath;
            var bin = new DirectoryInfo(binPath.TrimEnd('\\', '/'));
            var versionDir = bin.Parent;
            var eplanRoot = versionDir?.Parent?.Parent;
            if (versionDir != null && eplanRoot != null)
                yield return Path.Combine(eplanRoot.FullName, "Platform", versionDir.Name, bin.Name);
        }
    }
}
