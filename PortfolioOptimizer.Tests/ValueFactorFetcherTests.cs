using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NUnit.Framework;
using PortfolioOptimizer.App.Services;

namespace PortfolioOptimizer.Tests
{
    public class FakeHandler : HttpMessageHandler
    {
        private readonly string _response;
        public FakeHandler(string response) { _response = response; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
            return Task.FromResult(resp);
        }
    }

    public class ValueFactorFetcherTests
    {
        [Test]
        public async Task FetchAsync_ReturnsPriceToBook_WhenJsonContainsField()
        {
            // arrange: faux JSON similaire à celui retourné par l'API Yahoo Finance
            var json = "{\"quoteSummary\":{\"result\":[{\"defaultKeyStatistics\":{\"priceToBook\":{\"raw\":3.5}}}]}}";
            var handler = new FakeHandler(json);
            var http = new HttpClient(handler);
            var fetcher = new ValueFactorFetcher(http);

            // act: appeler FetchPbAtDateAsync
            var pb = await fetcher.FetchPbAtDateAsync("FAKE", DateTime.Today);

            // assert
            Assert.That(pb.HasValue, Is.True);
            Assert.That(pb.Value, Is.EqualTo(3.5).Within(1e-6));
        }
    }
}
