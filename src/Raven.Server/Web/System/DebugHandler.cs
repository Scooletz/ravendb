using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Web.System
{
    public sealed class DebugHandler : ServerRequestHandler
    {
        [RavenAction("/debug/routes", "GET", AuthorizationStatus.ValidUser, EndpointType.Read)]
        public async Task Routes()
        {
            var debugRoutes = Server.Router.AllRoutes
                .Where(x => x.IsDebugInformationEndpoint)
                .GroupBy(x => x.Path)
                .OrderBy(x => x.Key);

            var productionRoutes = Server.Router.AllRoutes
              .Where(x => x.IsDebugInformationEndpoint == false)
                .GroupBy(x => x.Path)
                .OrderBy(x => x.Key);

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("Debug");
                writer.WriteStartArray();
                var first = true;
                foreach (var route in debugRoutes)
                {
                    if (first == false)
                    {
                        writer.WriteComma();
                    }
                    first = false;

                    writer.WriteStartObject();
                    writer.WritePropertyName("Path");
                    writer.WriteString(route.Key);
                    writer.WriteComma();
                    writer.WritePropertyName("Methods");
                    writer.WriteString(string.Join(", ", route.Select(x => x.Method)));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteComma();
                writer.WritePropertyName("Production");
                writer.WriteStartArray();
                first = true;
                foreach (var route in productionRoutes)
                {
                    if (first == false)
                    {
                        writer.WriteComma();
                    }
                    first = false;

                    writer.WriteStartObject();
                    writer.WritePropertyName("Path");
                    writer.WriteString(route.Key);
                    writer.WriteComma();
                    writer.WritePropertyName("Methods");
                    writer.WriteString(string.Join(", ", route.Select(x => x.Method)));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
        }

        [RavenAction("/debug/routes/html", "GET", AuthorizationStatus.ValidUser, EndpointType.Read)]
        public async Task RoutesHtml()
        {
            // Get all GET routes
            var allGetRoutes = Server.Router.AllRoutes
                .Where(x => x.Method == "GET")
                .OrderBy(x => x.Path)
                .ToList();

            // Separate database-specific routes from non-database routes
            var databaseRoutes = allGetRoutes
                .Where(x => x.Path.StartsWith("/databases/*/"))
                .ToList();

            var nonDatabaseRoutes = allGetRoutes
                .Where(x => !x.Path.StartsWith("/databases/*/"))
                .ToList();

            // Get list of databases
            var databases = new List<string>();
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                foreach (var databaseName in ServerStore.Cluster.GetDatabaseNames(context))
                {
                    databases.Add(databaseName);
                }
            }

            // Generate HTML
            var html = GenerateRoutesHtml(nonDatabaseRoutes, databaseRoutes, databases);
            
            HttpContext.Response.ContentType = "text/html; charset=utf-8";
            await HttpContext.Response.WriteAsync(html);
        }

        private string GenerateRoutesHtml(List<RouteInformation> nonDatabaseRoutes, List<RouteInformation> databaseRoutes, List<string> databases)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>RavenDB Debug Routes</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            sb.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif; background: #f5f5f5; padding: 20px; }");
            sb.AppendLine("        .container { max-width: 1000px; margin: 0 auto; }");
            sb.AppendLine("        h1 { color: #333; margin-bottom: 30px; font-size: 28px; }");
            sb.AppendLine("        h2 { color: #555; margin: 30px 0 15px 0; font-size: 20px; border-bottom: 2px solid #ddd; padding-bottom: 8px; }");
            sb.AppendLine("        .routes-container { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); margin-bottom: 20px; }");
            sb.AppendLine("        .route-link { display: block; padding: 10px 15px; margin: 5px 0; background: #f9f9f9; border: 1px solid #e0e0e0; border-radius: 4px; color: #0066cc; text-decoration: none; transition: all 0.2s; }");
            sb.AppendLine("        .route-link:hover { background: #e8f4f8; border-color: #0066cc; transform: translateX(5px); }");
            sb.AppendLine("        .databases-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 15px; margin-top: 20px; }");
            sb.AppendLine("        .database-card { background: white; padding: 15px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine("        .database-card h3 { color: #0066cc; font-size: 16px; margin-bottom: 12px; padding-bottom: 8px; border-bottom: 1px solid #e0e0e0; word-break: break-word; }");
            sb.AppendLine("        .database-card .route-link { font-size: 13px; padding: 8px 12px; }");
            sb.AppendLine("        @media (max-width: 768px) {");
            sb.AppendLine("            .databases-grid { grid-template-columns: 1fr; }");
            sb.AppendLine("            .container { padding: 10px; }");
            sb.AppendLine("            h1 { font-size: 24px; }");
            sb.AppendLine("        }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine("        <h1>RavenDB Debug Routes (GET only)</h1>");
            
            // Non-database routes section
            sb.AppendLine("        <h2>Server Routes (No Database Required)</h2>");
            sb.AppendLine("        <div class=\"routes-container\">");
            foreach (var route in nonDatabaseRoutes)
            {
                var path = System.Net.WebUtility.HtmlEncode(route.Path);
                sb.AppendLine($"            <a class=\"route-link\" href=\"{path}\">{path}</a>");
            }
            sb.AppendLine("        </div>");
            
            // Database-specific routes section
            if (databases.Count > 0 && databaseRoutes.Count > 0)
            {
                sb.AppendLine("        <h2>Database-Specific Routes</h2>");
                sb.AppendLine("        <div class=\"databases-grid\">");
                
                foreach (var database in databases)
                {
                    var encodedDbName = System.Net.WebUtility.HtmlEncode(database);
                    sb.AppendLine("            <div class=\"database-card\">");
                    sb.AppendLine($"                <h3>{encodedDbName}</h3>");
                    
                    foreach (var route in databaseRoutes)
                    {
                        var path = route.Path.Replace("/databases/*/", $"/databases/{database}/");
                        var encodedPath = System.Net.WebUtility.HtmlEncode(path);
                        var displayPath = path.Replace($"/databases/{database}/", "");
                        var encodedDisplayPath = System.Net.WebUtility.HtmlEncode(displayPath);
                        sb.AppendLine($"                <a class=\"route-link\" href=\"{encodedPath}\" title=\"{encodedPath}\">{encodedDisplayPath}</a>");
                    }
                    
                    sb.AppendLine("            </div>");
                }
                
                sb.AppendLine("        </div>");
            }
            else if (databaseRoutes.Count > 0)
            {
                sb.AppendLine("        <h2>Database-Specific Routes</h2>");
                sb.AppendLine("        <div class=\"routes-container\">");
                sb.AppendLine("            <p style=\"color: #999; font-style: italic;\">No databases available. Create a database to see database-specific routes.</p>");
                sb.AppendLine("        </div>");
            }
            
            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }
    }
}
