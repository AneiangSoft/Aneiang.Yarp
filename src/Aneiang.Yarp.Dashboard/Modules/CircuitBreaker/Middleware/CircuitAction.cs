namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;

/// <summary>Internal action determined by circuit breaker lock evaluation.</summary>
internal enum CircuitAction { Proceed, RejectOpen, RejectHalfOpen }
