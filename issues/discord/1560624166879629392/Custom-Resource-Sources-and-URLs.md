**Custom Resource Sources and URLs in Downloads**

Allow users to extend **Settings → Downloads** with their own resource sources for plug-ins, sample packs, and AI models, alongside Nota’s built-in catalog.

**Add and Manage Sources**
- Provide an **Add Source** option for supported catalog or repository URLs.
- Let users name, edit, enable, disable, refresh, and remove their custom sources.
- Document the supported source formats. A simple, versioned catalog format would let developers or communities publish compatible resource lists.
- Distinguish a browsable catalog from a direct download link or a general website. Unsupported URLs should receive a clear explanation rather than silently failing.
- Optionally allow importing an individual compatible resource from a direct URL without requiring a complete catalog.

**Integrated Browsing**
- Make resources from enabled sources searchable and filterable in the existing Downloads interface.
- Clearly label each resource’s source, developer, version, type, and available compatibility information.
- Keep user-added sources distinguishable from Nota’s curated catalog.
- Handle duplicate resources without silently replacing their origin or installed version.

**Safe Downloads and Updates**
- Adding a source must not automatically download, install, or execute its contents.
- Validate catalog data, file types, supported platform/architecture, and available checksums or signatures before installation.
- Treat checksums as integrity checks, not proof that an unfamiliar source is trustworthy.
- Preserve the existing approach of not automatically running external installers or arbitrary scripts.
- Offer update information where the source supports it, with explicit user control over installing updates.
- Show useful errors for unavailable sources, broken links, incompatible resources, or failed validation.
- Removing a source should not delete already installed resources unless separately requested.

The goal is an extensible resource browser that supports personal and community collections without sacrificing clarity, compatibility, or control over what gets installed.
