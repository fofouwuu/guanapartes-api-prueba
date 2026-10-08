
using MySqlConnector;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// CONFIGURACIÓN CORS PARA VERCEL
// ==========================================

// Dirección de la página publicada en Vercel.
// Se configurará mediante una variable en Railway.
var origenVercel = builder.Configuration["Cors:OrigenVercel"];

// Direcciones permitidas durante el desarrollo local.
var origenesPermitidos = new List<string>
{
    "http://localhost:5500",
    "http://127.0.0.1:5500"
};

// Agregar la dirección pública de Vercel.
if (!string.IsNullOrWhiteSpace(origenVercel))
{
    origenesPermitidos.Add(
        origenVercel.Trim().TrimEnd('/')
    );
}

// Permitir solicitudes desde la página web.
builder.Services.AddCors(opciones =>
{
    opciones.AddPolicy("PaginaGuanapartes", politica =>
    {
        politica
            .WithOrigins(origenesPermitidos.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


// ==========================================
// CONFIGURACIÓN DE MYSQL
// ==========================================

// Lee la contraseña desde la configuración.
// En desarrollo puede utilizar user-secrets.
// En Railway utiliza las variables de entorno.
var contraseña = builder.Configuration["MySql:Password"];

if (contraseña is null)
{
    throw new InvalidOperationException(
        "Falta MySql:Password. Comprueba la configuración " +
        "de user-secrets o las variables de entorno."
    );
}

// Datos de conexión.
// En Railway se utilizan los valores configurados
// en las variables de entorno.
var configuracion = new MySqlConnectionStringBuilder
{
    Server = builder.Configuration["MySql:Server"] ?? "localhost",

    Port = builder.Configuration.GetValue<uint>(
        "MySql:Port", 3306
    ),

    Database = builder.Configuration["MySql:Database"]
        ?? "guanapartes_sa",

    UserID = builder.Configuration["MySql:User"] ?? "root",

    Password = contraseña
};

var cadenaConexion = configuracion.ConnectionString;


// ==========================================
// CREACIÓN DE LA APLICACIÓN
// ==========================================

var app = builder.Build();

// Activar CORS para permitir las conexiones
// desde la página publicada en Vercel.
app.UseCors("PaginaGuanapartes");


// ==========================================
// ARCHIVOS DE LA PÁGINA EN DESARROLLO
// ==========================================

// En tu computadora, la API también sirve
// los archivos de la página web.
//
// En Railway la página estará publicada
// por separado mediante Vercel.
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


// ==========================================
// MANEJO DE ERRORES MYSQL
// ==========================================

// Evita mostrar información sensible
// cuando ocurre un error de conexión.
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


// ==========================================
// PÁGINA INICIAL DE LA API
// ==========================================

app.MapGet("/", () =>
    "API de Guanapartes funcionando."
);


// ==========================================
// PRUEBA DE CONEXIÓN MYSQL
// ==========================================

app.MapGet("/api/prueba-conexion", async () =>
{
    await using var conexion =
        new MySqlConnection(cadenaConexion);

    await conexion.OpenAsync();

    await using var comando = new MySqlCommand(
        "SELECT COUNT(*) FROM productos;",
        conexion
    );

    var total = Convert.ToInt64(
        await comando.ExecuteScalarAsync()
    );

    return Results.Ok(new
    {
        conectado = true,

        mensaje = "Conexión con MySQL realizada correctamente.",

        totalProductos = total
    });
});


// ==========================================
// CONSULTA DE PRODUCTOS
// ==========================================

// Permite consultar el inventario de prueba
// con búsqueda, filtro de marca y paginación.
app.MapGet("/api/productos", async (
    int? pagina,
    string? buscar,
    string? marca) =>
{
    var paginaActual = pagina ?? 1;

    const int porPagina = 24;

    // Validar el número de página.
    if (paginaActual < 1)
    {
        return Results.BadRequest(new
        {
            mensaje = "La página debe ser 1 o mayor."
        });
    }

    var textoBusqueda = (buscar ?? "").Trim();
    var marcaSeleccionada = (marca ?? "").Trim();

    // Limitar la longitud de las búsquedas.
    if (textoBusqueda.Length > 150 ||
        marcaSeleccionada.Length > 100)
    {
        return Results.BadRequest(new
        {
            mensaje = "La búsqueda o la marca son demasiado largas."
        });
    }

    var desplazamiento =
        ((long)paginaActual - 1) * porPagina;

    await using var conexion =
        new MySqlConnection(cadenaConexion);

    await conexion.OpenAsync();


    // ======================================
    // FILTROS DE BÚSQUEDA
    // ======================================

    // Los valores se envían como parámetros
    // para evitar inyección SQL.
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


    // ======================================
    // CONTAR LOS RESULTADOS
    // ======================================

    await using var comandoTotal = new MySqlCommand(
        "SELECT COUNT(*) FROM productos " + filtros,
        conexion
    );

    comandoTotal.Parameters.AddWithValue(
        "@buscar", textoBusqueda
    );

    comandoTotal.Parameters.AddWithValue(
        "@marca", marcaSeleccionada
    );

    var totalProductos = Convert.ToInt64(
        await comandoTotal.ExecuteScalarAsync()
    );


    // ======================================
    // OBTENER LOS PRODUCTOS
    // ======================================

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

    await using var comando =
        new MySqlCommand(consulta, conexion);

    comando.Parameters.AddWithValue(
        "@buscar", textoBusqueda
    );

    comando.Parameters.AddWithValue(
        "@marca", marcaSeleccionada
    );

    comando.Parameters.AddWithValue(
        "@limite", porPagina
    );

    comando.Parameters.AddWithValue(
        "@desplazamiento", desplazamiento
    );


    // ======================================
    // CONSTRUIR LA RESPUESTA
    // ======================================

    var productos = new List<object>();

    await using var lector =
        await comando.ExecuteReaderAsync();

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

    // Devolver los productos y la paginación.
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


// ==========================================
// CONSULTA DE MARCAS
// ==========================================

// Devuelve las marcas existentes
// para utilizarlas en los filtros del catálogo.
app.MapGet("/api/marcas", async () =>
{
    await using var conexion =
        new MySqlConnection(cadenaConexion);

    await conexion.OpenAsync();

    const string consulta = """
        SELECT DISTINCT `MARCA`
        FROM productos
        WHERE `MARCA` IS NOT NULL
            AND TRIM(`MARCA`) <> ''
        ORDER BY `MARCA`;
        """;

    await using var comando =
        new MySqlCommand(consulta, conexion);

    await using var lector =
        await comando.ExecuteReaderAsync();

    var marcas = new List<string>();

    while (await lector.ReadAsync())
    {
        marcas.Add(lector.GetString(0));
    }

    return Results.Ok(marcas);
});


// ==========================================
// INICIAR LA API
// ==========================================

app.Run();
