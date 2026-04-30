using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyStock.Admin.Pages;

public abstract class AdminPageBase(AdminSessionService session) : PageModel
{
    protected AdminSessionService Session => session;

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (string.IsNullOrEmpty(session.GetToken()))
        {
            context.Result = new RedirectToPageResult("/Auth/Login");
            return;
        }
        base.OnPageHandlerExecuting(context);
    }

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        try
        {
            await next();
        }
        catch (SessionExpiredException)
        {
            session.ClearSession();
            context.HttpContext.Response.Redirect("/Auth/Login");
        }
    }
}
