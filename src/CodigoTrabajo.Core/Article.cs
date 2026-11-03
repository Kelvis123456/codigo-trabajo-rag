namespace CodigoTrabajo.Core;

/// <summary>Un artículo del Código (o uno de los principios fundamentales, con Number = 0).</summary>
public sealed record Article(int Number, string Label, string Context, string Text, IReadOnlyList<string> Notes);
