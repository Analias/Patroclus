﻿using System;
using Avalonia;
using Avalonia.Logging.Serilog;
using Patroclus.Avalonia.ViewModels;
using Patroclus.Avalonia.Views;

namespace Patroclus.Avalonia
{
    class Program
    {
        static void Main(string[] args)
        {
            BuildAvaloniaApp().Start<MainWindow>(() => new MainWindowViewModel());
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseReactiveUI()
                .LogToDebug();
    }
}
