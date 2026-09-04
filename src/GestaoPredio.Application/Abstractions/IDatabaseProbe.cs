namespace GestaoPredio.Application.Abstractions;
public interface IDatabaseProbe { Task<bool> CanConnectAsync(CancellationToken cancellationToken); }
