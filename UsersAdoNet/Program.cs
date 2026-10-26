using Microsoft.Data.SqlClient;
using System.Drawing;
using System.Reflection;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var configurationService = app.Services.GetService<IConfiguration>();
string connectionString = configurationService["ConnectionStrings:DefaultConnection"] ?? throw new InvalidOperationException("Connection string not found.");

app.Run(async (context) =>
{
    var response = context.Response;
    var request = context.Request;
    response.ContentType = "text/html; charset=utf-8";

    if (request.Path == "/" && request.Method == "GET")
    {
        string body = await GetAllUsers(connectionString);
        await response.WriteAsync(GenerateHtmlPage(body, "All Users"));
    }
    else if (request.Path == "/add" && request.Method == "GET")
    {
        await response.WriteAsync(GenerateHtmlPage(AddUserForm(), "Add User"));
    }
    else if (request.Path == "/add" && request.Method == "POST")
    {
        await AddUser(request, connectionString);
        response.Redirect("/");
    }
    else if (request.Path == "/delete" && request.Method == "POST")
    {
        await DeleteUser(request, connectionString);
        response.Redirect("/");
    }
    else if (request.Path == "/edit" && request.Method == "GET")
    {
        string body = await EditUserForm(request, connectionString);
        await response.WriteAsync(GenerateHtmlPage(body, "Edit User"));
    }
    else if (request.Path == "/edit" && request.Method == "POST")
    {
        await EditUser(request, connectionString);
        response.Redirect("/");
    }
    else if (request.Path == "/search" && request.Method == "GET")
    {
        string body = await SearchUsers(request, connectionString);
        await response.WriteAsync(GenerateHtmlPage(body, "Search Users"));
    }
    else if (request.Path == "/sort" && request.Method == "GET")
    {
        string body = await SortUsers(request, connectionString);
        await response.WriteAsync(GenerateHtmlPage(body, "Sort Users"));
    }
    else
    {
        response.StatusCode = 404;
        await response.WriteAsync("Page Not Found");
    }
});

app.Run();

static async Task<string> GetAllUsers(string connectionString)
{
    List<User> users = new List<User>();

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("SELECT Id, Name, Age FROM Users", connection);

        using (SqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                users.Add(new User(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }
    }

    string body = """
        <a href="/add">Add User</a>
        <h3>Search Users</h3>
        <form action="/search" method="get">
            <input type="text" name="name" placeholder="Enter name" required>
            <button type="submit">Search</button>
            <a href="/">Show All</a>
        </form>
        <h3>Sort Users</h3>
        <a href="/sort?by=name">Sort by Name</a>
        <a href="/sort?by=age">Sort by Age</a>
        """;

    body += BuildHtmlTable(users);
    return body;
}

static string AddUserForm()
{
    return """
        <form action="/add" method="post">
            <label>Name:</label>
            <input type="text" name="name" required>
            <label>Age:</label>
            <input type="number" name="age" min="0" required>
            <button type="submit">Add User</button>
        </form>
        <a href="/">Back</a>
        """;
}

static async Task AddUser(HttpRequest request, string connectionString)
{
    var form = await request.ReadFormAsync();
    string name = form["name"].ToString();

    if (string.IsNullOrWhiteSpace(name) || !int.TryParse(form["age"], out int age) || age < 0)
    {
        throw new ArgumentException("Invalid user data.");
    }

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("INSERT INTO Users (Name, Age) VALUES (@Name, @Age)", connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Age", age);
        await command.ExecuteNonQueryAsync();
    }
}

static async Task DeleteUser(HttpRequest request, string connectionString)
{
    var form = await request.ReadFormAsync();

    if (!int.TryParse(form["id"], out int id))
    {
        throw new ArgumentException("Invalid user ID.");
    }

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("DELETE FROM Users WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }
}

static async Task<string> EditUserForm(HttpRequest request, string connectionString)
{
    if (!int.TryParse(request.Query["id"], out int id))
    {
        return "<p>Invalid user ID.</p><a href='/'>Back</a>";
    }

    User? user = null;

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("SELECT Id, Name, Age FROM Users WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);

        using (SqlDataReader reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                user = new User(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2));
            }
        }
    }

    if (user == null)
    {
        return "<p>User not found.</p><a href='/'>Back</a>";
    }

    string name = System.Net.WebUtility.HtmlEncode(user.Name);

    return $"""
        <form action="/edit" method="post">
            <input type="hidden" name="id" value="{user.Id}">
            <label>Name:</label>
            <input type="text" name="name" value="{name}" required>
            <label>Age:</label>
            <input type="number" name="age" value="{user.Age}" min="0" required>
            <button type="submit">Save Changes</button>
        </form>
        <a href="/">Back</a>
        """;
}

static async Task EditUser(HttpRequest request, string connectionString)
{
    var form = await request.ReadFormAsync();

    if (!int.TryParse(form["id"], out int id) || !int.TryParse(form["age"], out int age) || age < 0)
    {
        throw new ArgumentException("Invalid user data.");
    }

    string name = form["name"].ToString();

    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException("Name cannot be empty.");
    }

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("UPDATE Users SET Name = @Name, Age = @Age WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Age", age);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }
}

static async Task<string> SearchUsers(HttpRequest request, string connectionString)
{
    string name = request.Query["name"].ToString();
    List<User> users = new List<User>();

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand("SELECT Id, Name, Age FROM Users WHERE Name LIKE @Name", connection);
        command.Parameters.AddWithValue("@Name", "%" + name + "%");

        using (SqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                users.Add(new User(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }
    }

    string body = "<a href='/'>Back to All Users</a>";
    body += BuildHtmlTable(users);
    return body;
}

static async Task<string> SortUsers(HttpRequest request, string connectionString)
{
    string sortBy = request.Query["by"].ToString();
    string sql = "SELECT Id, Name, Age FROM Users";

    if (sortBy == "name")
    {
        sql += " ORDER BY Name";
    }
    else if (sortBy == "age")
    {
        sql += " ORDER BY Age";
    }

    List<User> users = new List<User>();

    using (SqlConnection connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();
        SqlCommand command = new SqlCommand(sql, connection);

        using (SqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                users.Add(new User(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }
    }

    string body = "<a href='/'>Back to All Users</a>";
    body += BuildHtmlTable(users);
    return body;
}

static string GenerateHtmlPage(string body, string header)
{
    string html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>TITLE_PLACEHOLDER</title>
            <style>
                body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; }
                h2 { text-align: center; }
                table { width: 100%; border-collapse: collapse; margin-top: 20px; background: white; }
                th, td { border: 1px solid #ccc; padding: 10px; text-align: left; }
                th { background: #e5e5e5; }
                input { display: block; margin: 8px 0 15px; padding: 8px; }
                button, a { display: inline-block; margin: 5px 5px 5px 0; padding: 8px 12px; cursor: pointer; }
                a { color: #0645ad; }
                form { margin: 15px 0; }
            </style>
        </head>
        <body>
            <h2>HEADER_PLACEHOLDER</h2>
            BODY_PLACEHOLDER
        </body>
        </html>
        """;

    html = html.Replace("TITLE_PLACEHOLDER", System.Net.WebUtility.HtmlEncode(header));
    html = html.Replace("HEADER_PLACEHOLDER", System.Net.WebUtility.HtmlEncode(header));
    html = html.Replace("BODY_PLACEHOLDER", body);

    return html;
}

static string BuildHtmlTable<T>(IEnumerable<T> collection)
{
    StringBuilder tableHtml = new StringBuilder();
    tableHtml.Append("<table><thead><tr>");

    PropertyInfo[] properties = typeof(T).GetProperties();

    foreach (PropertyInfo property in properties)
    {
        tableHtml.Append($"<th>{System.Net.WebUtility.HtmlEncode(property.Name)}</th>");
    }

    tableHtml.Append("<th>Actions</th></tr></thead><tbody>");

    foreach (T item in collection)
    {
        tableHtml.Append("<tr>");

        foreach (PropertyInfo property in properties)
        {
            object? value = property.GetValue(item);
            string text = System.Net.WebUtility.HtmlEncode(value?.ToString() ?? "");
            tableHtml.Append($"<td>{text}</td>");
        }

        User user = (User)(object)item!;

        tableHtml.Append($"""
            <td>
                <a href="/edit?id={user.Id}">Edit</a>
                <form action="/delete" method="post" style="display:inline">
                    <input type="hidden" name="id" value="{user.Id}">
                    <button type="submit" onclick="return confirm('Delete this user?')">Delete</button>
                </form>
            </td>
            """);

        tableHtml.Append("</tr>");
    }

    tableHtml.Append("</tbody></table>");
    return tableHtml.ToString();
}

record User(int Id, string Name, int Age)
{
    public User(string Name, int Age) : this(0, Name, Age)
    {
    }
}
