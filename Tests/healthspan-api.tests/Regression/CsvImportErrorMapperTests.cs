using System.Reflection;
using FluentAssertions;
using MySqlConnector;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// t_a4651a49: per-row import errors must carry the real database reason, not
/// the wrapper text "Import batch insert failed". The mapper keys on
/// MySqlErrorCode (the same codes MySqlConnector surfaces from the real
/// server) and must never echo the raw MySQL message back to the client.
/// </summary>
public class CsvImportErrorMapperTests
{
    // MySqlException has no public ctor in MySqlConnector 2.4; build one via
    // reflection with the same (errorCode, sqlState, message) the connector
    // itself uses when the server replies (1062, 1452, 1406, ...).
    private static readonly ConstructorInfo MySqlExceptionCtor =
        typeof(MySqlException).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var ps = c.GetParameters();
                return ps.Length == 3
                    && ps[0].ParameterType == typeof(MySqlErrorCode)
                    && ps[1].ParameterType == typeof(string)
                    && ps[2].ParameterType == typeof(string);
            });

    private static MySqlException MySql(MySqlErrorCode code, string message) =>
        (MySqlException)MySqlExceptionCtor.Invoke(new object[] { code, null!, message });

    [Fact]
    public void Duplicate_key_on_import_id_reports_already_imported()
    {
        var ex = MySql(
            MySqlErrorCode.DuplicateKeyEntry,
            "Duplicate entry '0a1b2c3d-...' for key 'IX_Exercises_ClientEntityId'");

        var message = CsvImportErrorMapper.DescribeRowFailure(ex, 42);

        message.Should().Contain("ya fue importada");
        message.Should().NotContain("Import batch insert failed");
        message.Should().NotContain("IX_Exercises_ClientEntityId",
            "internal index names must not leak into the API response");
    }

    [Fact]
    public void Duplicate_key_on_other_index_names_the_index()
    {
        var ex = MySql(
            MySqlErrorCode.DuplicateKeyEntry,
            "Duplicate entry '2026-10-24|Training' for key 'IX_Workouts_DailySessionKey'");

        CsvImportErrorMapper.DescribeRowFailure(ex, 7)
            .Should().Contain("IX_Workouts_DailySessionKey");
    }

    [Fact]
    public void Foreign_key_violation_reports_integrity_reason()
    {
        var ex = MySql(
            MySqlErrorCode.NoReferencedRow2,
            "Cannot add or update a child row: a foreign key constraint fails "
            + "(`healthspan`.`Exercises`, CONSTRAINT `FK_Exercises_Workouts_WorkoutId` "
            + "FOREIGN KEY (`WorkoutId`) REFERENCES `Workouts` (`Id`))");

        var message = CsvImportErrorMapper.DescribeRowFailure(ex, 3);

        message.Should().Contain("restricción de integridad");
        message.Should().NotContain("FK_Exercises_Workouts_WorkoutId");
    }

    [Fact]
    public void Out_of_range_value_names_the_column()
    {
        // Real failure mode seen in QA t_79d39b1e: weight 1e999 -> Infinity.
        var ex = MySql(
            (MySqlErrorCode)1264, // DataOutOfRange
            "Out of range value for column 'Weight' at row 1");

        CsvImportErrorMapper.DescribeRowFailure(ex, 9)
            .Should().Contain("Weight").And.Contain("fuera del rango");
    }

    [Fact]
    public void Data_too_long_names_the_column()
    {
        var ex = MySql(
            (MySqlErrorCode)1406, // DataTooLong
            "Data too long for column 'QualityIntent' at row 1");

        CsvImportErrorMapper.DescribeRowFailure(ex, 12)
            .Should().Contain("QualityIntent").And.Contain("demasiado largo");
    }

    [Fact]
    public void Wrong_type_names_the_column()
    {
        var ex = MySql(
            MySqlErrorCode.TruncatedWrongValueForField, // 1366
            "Incorrect integer value: 'abc' for column 'Series' at row 1");

        CsvImportErrorMapper.DescribeRowFailure(ex, 5)
            .Should().Contain("Series").And.Contain("tipo correcto");
    }

    [Fact]
    public void Null_column_reports_missing_value()
    {
        var ex = MySql(
            MySqlErrorCode.ColumnCannotBeNull,
            "Column 'WorkoutId' cannot be null");

        CsvImportErrorMapper.DescribeRowFailure(ex, 8)
            .Should().Contain("WorkoutId").And.Contain("vacía");
    }

    [Fact]
    public void Non_finite_value_reports_invalid_number_with_the_value()
    {
        // Real server behavior (MySQL 8.4 via MySqlConnector 2.4): a
        // non-finite double param is rendered as the bare token Infinity.
        var ex = MySql(
            MySqlErrorCode.BadFieldError,
            "Unknown column 'Infinity' in 'field list'");

        var message = CsvImportErrorMapper.DescribeRowFailure(ex, 5);

        message.Should().Contain("Infinity").And.Contain("no es un número válido");
        message.Should().NotContain("Unknown column");
    }

    [Fact]
    public void Deadlock_suggests_retry()
    {
        var ex = MySql((MySqlErrorCode)1213, "Deadlock found when trying to get lock; try restarting transaction");

        CsvImportErrorMapper.DescribeRowFailure(ex, 4)
            .Should().Contain("Vuelve a intentarlo");
    }

    [Fact]
    public void Unwraps_MySqlException_nested_in_DbUpdateException()
    {
        var inner = MySql(
            (MySqlErrorCode)1264, // DataOutOfRange
            "Out of range value for column 'Weight' at row 1");
        var wrapped = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "Import batch insert failed", inner);

        var message = CsvImportErrorMapper.DescribeRowFailure(wrapped, 9);

        message.Should().Contain("Weight");
        message.Should().NotContain("Import batch insert failed");
    }

    [Fact]
    public void Unknown_database_error_degrades_to_generic_safe_message()
    {
        var ex = MySql((MySqlErrorCode)9999, "Some internal server detail super secret");

        var message = CsvImportErrorMapper.DescribeRowFailure(ex, 2);

        message.Should().Contain("error de la base de datos");
        message.Should().NotContain("super secret");
    }

    [Fact]
    public void Non_database_exception_degrades_to_generic_message()
    {
        var message = CsvImportErrorMapper.DescribeRowFailure(
            new InvalidOperationException("connection string leaked"), 1);

        message.Should().Contain("error interno");
        message.Should().NotContain("connection string");
    }
}
