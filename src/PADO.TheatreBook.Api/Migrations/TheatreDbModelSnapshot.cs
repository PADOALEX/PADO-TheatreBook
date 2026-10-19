using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace PADO.TheatreBook.Api.Migrations;

[DbContext(typeof(TheatreDb))]
public sealed class TheatreDbModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => TheatreDb.ConfigureModel(modelBuilder);
}
