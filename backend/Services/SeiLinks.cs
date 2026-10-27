using Microsoft.AspNetCore.WebUtilities;

namespace AssetManagement.Services;

public static class SeiLinks
{
    private const string SeiHost = "seibahia.ba.gov.br";

    public const string InvalidLinkMessage =
        "Link do SEI inválido. Cole o link de acesso direto do processo, copiado do SEI Bahia.";

    // SEI rejects links built outside the user's session, except the "acesso direto" link, which only needs the
    // internal process id. Only that id is kept, and the frontend rebuilds the link, so no arbitrary URL is stored.
    public static bool TryParseProcessId(string? link, out string processId)
    {
        processId = string.Empty;
        var value = (link ?? string.Empty).Trim();
        if (value.Length == 0)
            return true;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(string.Equals(uri.Host, SeiHost, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + SeiHost, StringComparison.OrdinalIgnoreCase)))
            return false;

        var query = QueryHelpers.ParseQuery(uri.Query);
        var id = query.TryGetValue("id_procedimento", out var procedimento) ? procedimento.ToString()
            : query.TryGetValue("id_protocolo", out var protocolo) ? protocolo.ToString()
            : string.Empty;
        if (id.Length == 0 || id.Length > 20 || !id.All(char.IsAsciiDigit))
            return false;

        processId = id;
        return true;
    }
}
