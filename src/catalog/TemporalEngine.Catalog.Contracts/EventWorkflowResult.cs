namespace TemporalEngine.Catalog.Contracts;

public record EventWorkflowResult(Guid EventId, IReadOnlyList<ProductResult> ProductResults);
