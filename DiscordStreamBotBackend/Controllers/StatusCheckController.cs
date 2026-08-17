using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace DiscordStreamBotBackend.Controllers
{
    [Route("[action]")]
    [ApiController]
    public class StatusCheckController : Controller
    {
        [EnableCors("allowGET")]
        [HttpGet]
        public ContentResult StatusCheck()
        {
            var result = new ContentResult();

            if (HttpContext.Request.Headers.Authorization != "Basic Enna_Alouette")
            {
                result.StatusCode = 403;
                result.Content = JsonConvert.SerializeObject(new { ErrorMessage = "403 禁止存取" });
            }
            else
            {
                result.StatusCode = 200;
                result.Content = "Ok";
            }

            return result;
        }
    }
}
