﻿using System.Net;
using NetCoreServer;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Vint.Core.Utils;

public static class LoggerUtils {
    static ILogger Logger { get; set; } = null!;

    static TemplateTheme Theme { get; } = new(new Dictionary<TemplateThemeStyle, string> {
        [TemplateThemeStyle.Text] = "\u001B[38;5;0253m",
        [TemplateThemeStyle.SecondaryText] = "\u001B[38;5;0246m",
        [TemplateThemeStyle.TertiaryText] = "\u001B[38;5;0242m",
        [TemplateThemeStyle.Invalid] = "\u001B[33;1m",
        [TemplateThemeStyle.Null] = "\u001B[38;5;0038m",
        [TemplateThemeStyle.Name] = "\u001B[38;5;0081m",
        [TemplateThemeStyle.String] = "\u001B[38;5;0216m",
        [TemplateThemeStyle.Number] = "\u001B[38;5;151m",
        [TemplateThemeStyle.Boolean] = "\u001B[38;5;0038m",
        [TemplateThemeStyle.Scalar] = "\u001B[38;5;0079m",
        [TemplateThemeStyle.LevelVerbose] = "\u001B[34m",
        [TemplateThemeStyle.LevelDebug] = "\u001b[36m",
        [TemplateThemeStyle.LevelInformation] = "\u001B[32m",
        [TemplateThemeStyle.LevelWarning] = "\u001B[33;1m",
        [TemplateThemeStyle.LevelError] = "\u001B[31;1m",
        [TemplateThemeStyle.LevelFatal] = "\u001B[31;1m"
    });

    public static void Initialize(LogEventLevel logEventLevel) {
        const string template =
            "[{@t:HH:mm:ss.fff}] [{@l}] [{SourceContext}] {#if SessionEndpoint is not null}[{SessionEndpoint}] {#end}{@m:lj}\n{@x}";

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console(new ExpressionTemplate(template, theme: Theme))
            .WriteTo.File(new ExpressionTemplate(template),
                "Vint.log",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true)
            .MinimumLevel.Is(logEventLevel)
            .CreateLogger();

        Logger = Log.Logger.ForType(typeof(LoggerUtils));

        Logger.Information("Logger initialized");
    }

    public static ILogger ForType(this ILogger logger, Type type) =>
        logger.ForContext(Constants.SourceContextPropertyName, type.Name);

    public static ILogger WithPlayer(this ILogger logger, TcpSession session) =>
        logger.ForContext("SessionEndpoint", session.Socket.RemoteEndPoint as IPEndPoint);
}