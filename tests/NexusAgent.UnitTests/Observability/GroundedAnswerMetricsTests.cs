using System.Diagnostics.Metrics;
using NexusAgent.Application.Observability;
using Xunit;

namespace NexusAgent.UnitTests.Observability;

public sealed class GroundedAnswerMetricsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    public void Token_estimate_rounds_up_every_four_characters(
        int characters,
        long expected)
    {
        Assert.Equal(
            expected,
            GroundedAnswerMetrics.EstimateTokens(characters));
    }

    [Fact]
    public void Negative_character_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GroundedAnswerMetrics.EstimateTokens(-1));
    }

    [Fact]
    public void Generation_estimate_uses_configured_rates()
    {
        var estimate = GroundedAnswerMetrics.RecordGenerationEstimate(
            "12345678",
            ["1234"],
            "12345678",
            inputUsdPerMillion: 2m,
            outputUsdPerMillion: 4m);

        Assert.Equal(3, estimate.EstimatedInputTokens);
        Assert.Equal(2, estimate.EstimatedOutputTokens);
        Assert.Equal(0.000014m, estimate.EstimatedCostUsd);
    }

    [Fact]
    public void Default_rates_do_not_claim_actual_billing()
    {
        var estimate = GroundedAnswerMetrics.RecordGenerationEstimate(
            "question",
            ["evidence"],
            "answer");

        Assert.Equal(0m, estimate.EstimatedCostUsd);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 2)]
    public void Invalid_retrieval_counts_are_rejected(
        int retrieved,
        int safe)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GroundedAnswerMetrics.RecordRetrieval(retrieved, safe));
    }

    [Fact]
    public void Retrieval_metrics_report_candidate_retention()
    {
        var observed = new Dictionary<string, double>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name ==
                    GroundedAnswerMetrics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                observed[instrument.Name] = measurement;
            });

        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
            {
                observed[instrument.Name] = measurement;
            });

        listener.Start();

        GroundedAnswerMetrics.RecordRetrieval(5, 3);

        Assert.Equal(
            5d,
            observed["grounded_answer.retrieved_candidates"]);

        Assert.Equal(
            3d,
            observed["grounded_answer.safe_candidates"]);

        Assert.Equal(
            2d,
            observed["grounded_answer.rejected_candidates"]);

        Assert.Equal(
            0.6d,
            observed["grounded_answer.evidence_retention_ratio"],
            precision: 8);
    }
}