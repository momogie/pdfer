using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.HtmlProcessing.TagHandlers;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Infrastructure.Caching;
using PdfEr.Infrastructure.PdfWriters;
using PdfEr.Infrastructure.Typography;
using PdfEr.Infrastructure.Utilities;

namespace PdfEr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPdfEr(this IServiceCollection services, Action<PdfConverterConfiguration>? configure = null)
    {
        var config = new PdfConverterConfiguration();
        configure?.Invoke(config);

        services.AddSingleton(config);
        services.AddSingleton<IUnitConverter, UnitConverter>();
        services.AddSingleton<ICacheService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileCacheService>>();
            return new FileCacheService(config.TempDirectory, logger, config.CacheCleanupIntervalSeconds);
        });

        services.AddSingleton<IFontRegistry>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FontRegistry>>();
            var registry = new FontRegistry(logger, config.FontDirectories);
            foreach (var dir in config.FontDirectories ?? [])
                registry.RegisterDirectory(dir);
            return registry;
        });
        services.AddSingleton<ITextLayoutEngine, TextLayoutEngine>();
        services.AddSingleton<CssParser>();
        services.AddSingleton<CssMerger>();
        services.AddSingleton<CssNormalizer>();
        services.AddSingleton<ColorParser>();
        services.AddSingleton<HtmlParser>();
        services.AddSingleton<LayoutEngine>();

        services.AddSingleton<ITagHandler, DivHandler>();
        services.AddSingleton<ITagHandler, ParagraphHandler>();
        services.AddSingleton<ITagHandler, HeadingHandler>();
        services.AddSingleton<ITagHandler, ListHandler>();
        services.AddSingleton<ITagHandler, ListItemHandler>();
        services.AddSingleton<ITagHandler, BlockquoteHandler>();
        services.AddSingleton<ITagHandler, PreHandler>();
        services.AddSingleton<ITagHandler, HrHandler>();
        services.AddSingleton<ITagHandler, BreakHandler>();
        services.AddSingleton<ITagHandler, BoldHandler>();
        services.AddSingleton<ITagHandler, ItalicHandler>();
        services.AddSingleton<ITagHandler, UnderlineHandler>();
        services.AddSingleton<ITagHandler, SpanHandler>();
        services.AddSingleton<ITagHandler, AnchorHandler>();
        services.AddSingleton<ITagHandler, CodeHandler>();
        services.AddSingleton<ITagHandler, SubScriptHandler>();
        services.AddSingleton<ITagHandler, SuperScriptHandler>();
        services.AddSingleton<ITagHandler, SmallHandler>();
        services.AddSingleton<ITagHandler, MarkHandler>();
        services.AddSingleton<ITagHandler, DelHandler>();
        services.AddSingleton<ITagHandler, QuoteHandler>();
        services.AddSingleton<ITagHandler, AbbrHandler>();
        services.AddSingleton<ITagHandler, TableHandler>();
        services.AddSingleton<ITagHandler, TableRowHandler>();
        services.AddSingleton<ITagHandler, TableCellHandler>();
        services.AddSingleton<ITagHandler, TableSectionHandler>();

        services.AddSingleton<TagRegistry>();
        services.AddSingleton<PdfWriter>();
        services.AddSingleton<IPdfConverter, PdfConverterService>();

        return services;
    }
}
