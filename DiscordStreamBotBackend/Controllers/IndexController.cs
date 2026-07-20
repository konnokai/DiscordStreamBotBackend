using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using DiscordStreamBotBackend.Services;

namespace DiscordStreamBotBackend.Controllers
{
    [Route("/")]
    [ApiController]
    public class IndexController : Controller
    {
        private readonly PublicUrlService _publicUrls;

        public IndexController(PublicUrlService publicUrls)
        {
            _publicUrls = publicUrls;
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public IActionResult Index()
        {
            return Redirect(_publicUrls.FrontendDomain);
        }
    }
}
