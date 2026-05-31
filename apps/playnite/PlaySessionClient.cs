using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Playdex
{
    public class PlaySessionClient
    {
        private const string Host = "playdex.ejwan.dev";

        private static string Endpoint (string action) => "api/playnite/sessions" + '/' +  action;

        private readonly HttpClient _httpClient;

        public PlaySessionClient(string bearerToken)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://" + Host);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        public async Task StartSessionAsync(string gameTitle, string gameId, string gameProvider)
        {
            var content = new StringContent(
                BuildPayload(gameTitle, gameId, gameProvider),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(Endpoint("start"), content);

            response.EnsureSuccessStatusCode();
        }
        
        public async Task EndSessionAsync()
        {
            var content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(Endpoint("end"), content);

            response.EnsureSuccessStatusCode();
        }

        private static string BuildPayload(string gameTitle, string gameId, string gameProvider)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"gameTitle\":\"")
                .Append(JsonEscape(gameTitle))
                .Append("\",");

            sb.Append("\"gameId\":\"")
                .Append(JsonEscape(gameId))
                .Append("\",");

            sb.Append("\"gameProvider\":");

            if (gameProvider == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("\"")
                    .Append(JsonEscape(gameProvider))
                    .Append("\"");
            }

            sb.Append("}");

            return sb.ToString();
        }
        
        private static string JsonEscape(string value)
        {
            if (value == null)
                return null;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}