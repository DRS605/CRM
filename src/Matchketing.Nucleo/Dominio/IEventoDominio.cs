namespace Matchketing.Nucleo.Dominio;

/// <summary>Algo que ha ocurrido en el dominio y que otros módulos pueden querer saber.</summary>
public interface IEventoDominio
{
    DateTimeOffset OcurridoEn { get; }
}
