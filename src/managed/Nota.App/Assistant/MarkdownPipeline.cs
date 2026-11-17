// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Markdig pipeline the Assistant panel parses replies with (specs/098, FR-012): pipe tables,
// GitHub alerts (the manual's note / tip / caution / danger asides), strikethrough and bare
// links. No Avalonia here: the smoke test compiles this file directly and checks what it parses.

using Markdig;

namespace Nota.App;

internal static class AssistantMarkdown
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAlertBlocks()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();
}
