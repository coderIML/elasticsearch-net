﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Xunit.XunitPlumbing;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Tests.Core.Client.Settings;
using Tests.Core.ManagedElasticsearch.Clusters;
using Tests.Domain;

namespace Tests.Document.Multiple.MultiGet
{
	public class GetManyApiTests : IClusterFixture<WritableCluster>
	{
		private readonly IElasticClient _client;
		private readonly IEnumerable<long> _ids = Developer.Developers.Select(d => d.Id).Take(10);

		public GetManyApiTests(WritableCluster cluster) => _client = cluster.Client;

		[I] public void UsesDefaultIndexAndInferredType()
		{
			var response = _client.GetMany<Developer>(_ids);
			response.Count().Should().Be(10);
			foreach (var hit in response)
			{
				hit.Index.Should().NotBeNullOrWhiteSpace();
				hit.Id.Should().NotBeNullOrWhiteSpace();
				hit.Found.Should().BeTrue();
			}
		}

		[I] public async Task UsesDefaultIndexAndInferredTypeAsync()
		{
			var response = await _client.GetManyAsync<Developer>(_ids);
			response.Count().Should().Be(10);
			foreach (var hit in response)
			{
				hit.Index.Should().NotBeNullOrWhiteSpace();
				hit.Id.Should().NotBeNullOrWhiteSpace();
				hit.Found.Should().BeTrue();
			}
		}

		[I] public async Task ReturnsDocsMatchingIds()
		{
			var id = _ids.First();

			var response = await _client.GetManyAsync<Developer>(new[] { id, id, id });
			response.Count().Should().Be(3);
			foreach (var hit in response)
			{
				hit.Index.Should().NotBeNullOrWhiteSpace();
				hit.Id.Should().Be(id.ToString(CultureInfo.InvariantCulture));
				hit.Found.Should().BeTrue();
			}
		}

		[I] public void ReturnsDocsMatchingIdsFromDifferentIndices()
		{
			var developerIndex = Nest.Indices.Index<Developer>();
			var indexName = developerIndex.GetString(_client.ConnectionSettings);

			var reindexResponse = _client.ReindexOnServer(r => r
				.Source(s => s
					.Index(developerIndex)
					.Query<Developer>(q => q
						.Ids(ids => ids.Values(_ids))
					)
				)
				.Destination(d => d
					.Index($"{indexName}-reindex"))
				.Refresh()
			);

			if (!reindexResponse.IsValid)
				throw new Exception($"problem reindexing documents for integration test: {reindexResponse.DebugInformation}");

			var id = _ids.First();

			var multiGetResponse = _client.MultiGet(s => s
				.RequestConfiguration(r => r.ThrowExceptions())
				.Get<Developer>(m => m
					.Id(id)
					.Index(indexName)
				)
				.Get<Developer>(m => m
					.Id(id)
					.Index($"{indexName}-reindex")
				)
			);

			var response = multiGetResponse.GetMany<Developer>(new [] { id, id });

			response.Count().Should().Be(4);
			foreach (var hit in response)
			{
				hit.Index.Should().NotBeNullOrWhiteSpace();
				hit.Id.Should().NotBeNullOrWhiteSpace();
				hit.Found.Should().BeTrue();
			}
		}

		[I] public async Task ReturnsSourceMatchingIds()
		{
			var id = _ids.First();

			var sources = await _client.SourceManyAsync<Developer>(new[] { id, id, id });
			sources.Count().Should().Be(3);
			foreach (var hit in sources)
			{
				hit.Id.Should().Be(id);
			}
		}

		[I] public async Task CanHandleNotFoundResponses()
		{
			var response = await _client.GetManyAsync<Developer>(_ids.Select(i => i * 100));
			response.Count().Should().Be(10);
			foreach (var hit in response)
			{
				hit.Index.Should().NotBeNullOrWhiteSpace();
				hit.Id.Should().NotBeNullOrWhiteSpace();
				hit.Found.Should().BeFalse();
			}
		}

		[I] public void ThrowsExceptionOnConnectionError()
		{
			if (TestConnectionSettings.RunningFiddler) return; //fiddler meddles here

			var client = new ElasticClient(new TestConnectionSettings(port: 9500));
			Action response = () => client.GetMany<Developer>(_ids.Select(i => i * 100));
			response.Should().Throw<ElasticsearchClientException>();
		}
	}
}
