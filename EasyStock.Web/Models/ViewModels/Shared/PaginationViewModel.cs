namespace EasyStock.Web.Models.ViewModels.Shared;

public class PaginationViewModel
{
    public int Page { get; set; }
    public int Pages { get; set; }
    public int Total { get; set; }
    public int Limit { get; set; }

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < Pages;
}
