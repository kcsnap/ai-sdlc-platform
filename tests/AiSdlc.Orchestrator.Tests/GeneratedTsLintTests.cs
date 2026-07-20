using AiSdlc.Orchestrator.Builds;
using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

/// <summary>
/// D13 (TDD red-first): generated frontend code interpolated an imported FUNCTION into a template
/// literal instead of calling it — useBookings.ts fetched `${apiUrl}/api/bookings`, the browser hit
/// "/function%20Rh(n){…}/api/bookings", the SPA fallback answered 200, and the live UI silently showed
/// no data. TypeScript does not flag `${fn}`; this lint kills the class at the commit seam.
/// </summary>
public sealed class GeneratedTsLintTests
{
    // The VERBATIM defect from user-app-c6348eab src/frontend/src/hooks/useBookings.ts.
    private const string BookingHook = """
        import { apiUrl } from '../lib/api';

        export function useBookings() {
          const load = async () => {
            const res = await fetch(`${apiUrl}/api/bookings`);
            return res.json();
          };
          const create = (body: unknown) => fetch(`${apiUrl}/api/bookings`, { method: 'POST', body: JSON.stringify(body) });
          const cancel = (id: string) => fetch(`${apiUrl}/api/bookings/${id}`, { method: 'DELETE' });
          return { load, create, cancel };
        }
        """;

    [Fact]
    public void Flags_every_interpolation_of_an_imported_function_identifier()
    {
        var violations = GeneratedTsLint.Scan(BookingHook);

        Assert.Equal(3, violations.Count); // list / create / cancel
        Assert.All(violations, v => Assert.Contains("apiUrl", v.Excerpt));
    }

    [Fact]
    public void Allows_the_correct_called_form_and_ordinary_variables()
    {
        const string correct = """
            import { apiUrl } from '../lib/api';
            const baseUrl = 'https://x.example';
            const a = fetch(`${apiUrl('/api/bookings')}`);
            const b = fetch(`${baseUrl}/api/bookings`);
            const c = `${window.location.origin}/api/x`;
            """;

        Assert.Empty(GeneratedTsLint.Scan(correct));
    }

    [Fact]
    public void Only_imported_identifiers_are_candidates()
    {
        // apiUrl interpolated bare, but NOT imported in this file — a local string is legitimate.
        const string local = """
            const apiUrl = 'https://func-x.azurewebsites.net';
            const r = fetch(`${apiUrl}/api/things`);
            """;

        Assert.Empty(GeneratedTsLint.Scan(local));
    }

    [Theory]
    [InlineData("src/frontend/src/hooks/useBookings.ts", true)]
    [InlineData("src/frontend/src/components/App.tsx", true)]
    [InlineData("src/api/Program.cs", false)]  // not TS — never scanned
    [InlineData("index.html", false)]
    public void IsRejectedGeneratedTs_gates_ts_and_tsx_only(string path, bool scanned)
    {
        var change = new FileChange(path, BookingHook);

        Assert.Equal(scanned, GeneratedTsLint.IsRejectedGeneratedTs(change));
    }
}
