using System.Net;
using System.Net.Http.Json;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;

namespace ClaimFlow.Claims.Application;

/// <summary>Port to the Policy service (synchronous REST — the claimant waits for the answer).</summary>
public interface IPolicyClient
{
    Task<CoverageCheckResult> CheckCoverageAsync(string policyNumber, DateOnly incidentDate, ClaimType type, CancellationToken ct);
}

public sealed class HttpPolicyClient(HttpClient http, ILogger<HttpPolicyClient> log) : IPolicyClient
{
    public async Task<CoverageCheckResult> CheckCoverageAsync(string policyNumber, DateOnly incidentDate, ClaimType type, CancellationToken ct)
    {
        var url = $"/policies/{Uri.EscapeDataString(policyNumber)}/coverage?incidentDate={incidentDate:yyyy-MM-dd}&type={type}";
        Exception? last = null;

        // Tiny retry loop for transient failures (a library such as Polly would replace this in a bigger system).
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var res = await http.GetAsync(url, ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized)
                    throw new ServiceUnavailableException("POLICY_SERVICE_MISCONFIGURED", "Policy verification is misconfigured. Please contact support.");
                if ((int)res.StatusCode >= 500) { last = new HttpRequestException($"Policy service answered {(int)res.StatusCode}"); }
                else
                {
                    res.EnsureSuccessStatusCode();
                    return (await res.Content.ReadFromJsonAsync<CoverageCheckResult>(JsonDefaults.Options, ct))!;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
            }

            log.LogWarning("Policy service call failed (attempt {Attempt}/3): {Error}", attempt, last?.Message);
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
        }

        throw new ServiceUnavailableException("POLICY_SERVICE_UNAVAILABLE",
            "We can't verify your policy right now. Please try again in a moment.");
    }
}

/// <summary>Who can be assigned claims. Demo shortcut: a real system would ask Identity / HR.</summary>
public sealed class StaffDirectory
{
    public (string Id, string Name)? Find(string id) =>
        DemoCatalog.Users.Where(u => u.Role != Roles.Claimant).Where(u => u.Id == id).Select(u => ((string, string)?)(u.Id, u.Name)).FirstOrDefault();
}
