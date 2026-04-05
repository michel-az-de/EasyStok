namespace EasyStock.Web.Models.Api;

public record PagedResult<T>
{
    public required List<T> Data { get; init; }
    public required Meta Meta { get; init; }
}

public record Meta(int Total, int Page, int Pages, int Limit);
