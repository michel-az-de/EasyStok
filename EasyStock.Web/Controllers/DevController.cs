using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

// Storybook interno do Design System (T2.14).
// Acessível apenas em Development; sem auth (não inclui dados de tenant).
[AllowAnonymous]
[Route("dev")]
public class DevController(IWebHostEnvironment env) : Controller
{
    [HttpGet("components")]
    public IActionResult Components()
    {
        if (!env.IsDevelopment()) return NotFound();
        ViewBag.Title = "DS Components";
        return View();
    }
}
