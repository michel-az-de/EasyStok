using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("error/{code:int}")]
    public IActionResult Index(int code)
    {
        if (code == 404)
            return View("NotFound");

        return View("Error");
    }
}
