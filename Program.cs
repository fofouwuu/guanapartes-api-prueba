using MySqlConnector;
using Microsoft.Extensions.FileProviders;
var builder = WebApplication.CreateBuilder(args);

// Lee la contraseña guardada con user-secrets.
var contraseña = builder.Configuration["MySql:Password"];

if (contraseña is null)
{
    throw new InvalidOperationException(
        "Falta MySql:Password. Ejecuta el proyecto en Development " +
        "y comprueba la configuración de user-secrets."
    );
}

// Datos de conexión con la base local.
var configuracion = new MySqlConnectionStringBuilder
{
    Server = builder.Configuration["MySql:Server"] ?? "localhost",
    Port = builder.Configuration.GetValue<uint>("MySql:Port", 3306),
    Database = builder.Configuration["MySql:Database"] ?? "guanapartes_sa",
    UserID = builder.Configuration["MySql:User"] ?? "root",
    Password = contraseña
};

var cadenaConexion = configuracion.ConnectionString;

var app = builder.Build();

// En tu computadora, la API también sirve los archivos de la página.
// En el servidor, la página estará publicada por separado en Vercel.
if (app.Environment.IsDevelopment())
{
    var archivosPagina = new PhysicalFileProvider(
        @"C:\Users\Sofi Cabezas\OneDrive\Documentos\Prueba 1 guana"
    );

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = archivosPagina
    });
} 
// Maneja errores de MySQL sin mostrar la contraseña.
app.Use(async (contexto, siguiente) =>
{
    try
    {
        await siguiente(contexto);
    }
    catch (MySqlException error)
    {
        app.Logger.LogError(
            error,
            "Error al consultar la base de datos."
        );

        contexto.Response.StatusCode = 500;

        await contexto.Response.WriteAsJsonAsync(new
        {
            mensaje = "No se pudo consultar la base de datos.",
            codigo = error.Number
        });
    }
});

// Página inicial del backend.
app.MapGet("/", () => "API de Guanapartes funcionando.");

// Conservamos la prueba de conexión.
app.MapGet("/api/prueba-conexion", async () =>
{
    await using var conexion = new MySqlConnection(cadenaConexion);
    await conexion.OpenAsync();

    await using var comando = new MySqlCommand(
        "SELECT COUNT(*) FROM productos;",
        conexion
    );

    var total = Convert.ToInt64(await comando.ExecuteScalarAsync());

    return Results.Ok(new
    {
        conectado = true,
        mensaje = "Conexión con MySQL realizada correctamente.",
        totalProductos = total
    });
});

// Consulta productos con búsqueda, marca y paginación.
app.MapGet("/api/productos", async (
    int? pagina,
    string? buscar,
    string? marca) =>
{
    var paginaActual = pagina ?? 1;
    const int porPagina = 24;

    if (paginaActual < 1)
    {
        return Results.BadRequest(new
        {
            mensaje = "La página debe ser 1 o mayor."
        });
    }

    var textoBusqueda = (buscar ?? "").Trim();
    var marcaSeleccionada = (marca ?? "").Trim();

    if (textoBusqueda.Length > 150 || marcaSeleccionada.Length > 100)
    {
        return Results.BadRequest(new
        {
            mensaje = "La búsqueda o la marca son demasiado largas."
        });
    }

    var desplazamiento = ((long)paginaActual - 1) * porPagina;

    await using var conexion = new MySqlConnection(cadenaConexion);
    await conexion.OpenAsync();

    // Los valores se envían como parámetros.
    // No se concatenan dentro de la consulta SQL.
    const string filtros = """
        WHERE
            (
                @buscar = ''
                OR LOCATE(@buscar, `DESCRIPCIÓN`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ORIGINAL`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ALTERNO 1`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ALTERNO 2`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ALTERNO 3`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ALTERNO 4`) > 0
                OR LOCATE(@buscar, `NÚMERO PARTE ALTERNO 5`) > 0
                OR LOCATE(@buscar, `MARCA`) > 0
            )
            AND (@marca = '' OR `MARCA` = @marca)
        """;

    // Cuenta todos los resultados que cumplen los filtros.
    await using var comandoTotal = new MySqlCommand(
        "SELECT COUNT(*) FROM productos " + filtros,
        conexion
    );

    comandoTotal.Parameters.AddWithValue("@buscar", textoBusqueda);
    comandoTotal.Parameters.AddWithValue("@marca", marcaSeleccionada);

    var totalProductos = Convert.ToInt64(
        await comandoTotal.ExecuteScalarAsync()
    );

    // Devuelve únicamente los datos del catálogo.
    var consulta = """
        SELECT
            `ITEM`,
            `DESCRIPCIÓN`,
            `MARCA`,
            `NÚMERO PARTE ORIGINAL`,
            `CANT`,
            `PRECIO PÚBLICO`
        FROM productos
        """ + "\n" + filtros + """

        
        ORDER BY `ITEM`
        LIMIT @limite OFFSET @desplazamiento;
        """;

    await using var comando = new MySqlCommand(consulta, conexion);

    comando.Parameters.AddWithValue("@buscar", textoBusqueda);
    comando.Parameters.AddWithValue("@marca", marcaSeleccionada);
    comando.Parameters.AddWithValue("@limite", porPagina);
    comando.Parameters.AddWithValue("@desplazamiento", desplazamiento);

    var productos = new List<object>();

    await using var lector = await comando.ExecuteReaderAsync();

    while (await lector.ReadAsync())
    {
        productos.Add(new
        {
            id = lector.GetInt32(0),

            descripcion = lector.IsDBNull(1)
                ? ""
                : lector.GetString(1),

            marca = lector.IsDBNull(2)
                ? ""
                : lector.GetString(2),

            numeroParte = lector.IsDBNull(3)
                ? ""
                : lector.GetString(3),

            cantidad = lector.IsDBNull(4)
                ? (int?)null
                : lector.GetInt32(4),

            precioPublico = lector.IsDBNull(5)
                ? (decimal?)null
                : lector.GetDecimal(5)
        });
    }

    return Results.Ok(new
    {
        pagina = paginaActual,
        porPagina,
        totalProductos,
        totalPaginas = (long)Math.Ceiling(
            totalProductos / (double)porPagina
        ),
        productos
    });
});

// Marcas existentes para el futuro filtro del catálogo.
app.MapGet("/api/marcas", async () =>
{
    await using var conexion = new MySqlConnection(cadenaConexion);
    await conexion.OpenAsync();

    const string consulta = """
        SELECT DISTINCT `MARCA`
        FROM productos
        WHERE `MARCA` IS NOT NULL
            AND TRIM(`MARCA`) <> ''
        ORDER BY `MARCA`;
        """;

    await using var comando = new MySqlCommand(consulta, conexion);
    await using var lector = await comando.ExecuteReaderAsync();

    var marcas = new List<string>();

    while (await lector.ReadAsync())
    {
        marcas.Add(lector.GetString(0));
    }

    return Results.Ok(marcas);
});

app.Run();