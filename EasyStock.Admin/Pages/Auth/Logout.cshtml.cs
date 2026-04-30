using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyStock.Admin.Pages.Auth;

public class LogoutModel(AdminSessionService session) : PageModel
{
    public IActionResult OnPost()
    {
        session.ClearSession();
        return RedirectToPage("/Auth/Login");
    }
}
