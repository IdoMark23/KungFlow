using System;
using System.Net.Http;
using System.Threading.Tasks;
class P {
  static async Task Main() {
    var handler = new HttpClientHandler();
    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    using var client = new HttpClient(handler);
    var resp = await client.GetAsync("https://localhost:8443/web");
    Console.WriteLine((int)resp.StatusCode);
    Console.WriteLine(await resp.Content.ReadAsStringAsync());
  }
}
