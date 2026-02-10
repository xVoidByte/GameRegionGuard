using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GameRegionGuard.Services
{
    public class IPDownloadService
    {
        // ipdeny.com offers IP ranges in Russia
        private const string IpDenyUrl = "https://www.ipdeny.com/ipblocks/data/countries/ru.zone";

        public async Task<List<string>> DownloadIPRangesAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info($"Downloading IP ranges from: {IpDenyUrl}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(2);

                    var response = await client.GetAsync(IpDenyUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();

                    var ranges = content
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Where(line => line.Contains("/"))
                        .Select(line => line.Trim())
                        .ToList();

                    Logger.Info($"Successfully downloaded {ranges.Count} IP ranges");
                    Logger.Debug($"Sample ranges: {string.Join(", ", ranges.Take(3))}");

                    return ranges;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("IP download cancelled by user");
                throw;
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"HTTP error downloading IP ranges: {ex.Message}");
                throw new Exception($"Failed to download IP ranges: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error downloading IP ranges: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }
}