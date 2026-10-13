using System.Text;
using Fonia.Data;

namespace Fonia.Data.Tests;

public class CsvTableTests
{
    private static CsvTable Read(string text) => CsvTable.Read(new StringReader(text));

    [Fact]
    public void A_header_and_rows_are_read_by_column_name()
    {
        var table = Read("""
            ident,name,elevation_ft
            SBCT,Curitiba,2988
            SBGL,Galeão,28
            """);

        table.Columns.ShouldBe(["ident", "name", "elevation_ft"]);
        table.Rows.Count.ShouldBe(2);
        table.Rows[0]["ident"].ShouldBe("SBCT");
        table.Rows[1]["name"].ShouldBe("Galeão");
        table.Rows[1]["elevation_ft"].ShouldBe("28");
    }

    [Fact]
    public void Quoted_fields_may_contain_commas_quotes_and_line_breaks()
    {
        var table = Read("id,name,note\n1,\"Rio, Galeão\",\"he said \"\"hi\"\"\"\n2,\"two\nlines\",x\n");

        table.Rows[0]["name"].ShouldBe("Rio, Galeão");
        table.Rows[0]["note"].ShouldBe("he said \"hi\"");
        table.Rows[1]["name"].ShouldBe("two\nlines");
        table.Rows[1]["note"].ShouldBe("x");
    }

    [Fact]
    public void Empty_fields_are_empty_strings()
    {
        var table = Read("a,b,c\n1,,3\n,,\n");

        table.Rows[0]["b"].ShouldBe(string.Empty);
        table.Rows[1]["a"].ShouldBe(string.Empty);
        table.Rows[1]["c"].ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("a,b\r\n1,2\r\n3,4\r\n")]
    [InlineData("a,b\n1,2\n3,4\n")]
    [InlineData("a,b\r1,2\r3,4\r")]
    [InlineData("a,b\n1,2\n3,4")] // no line break at the end
    public void Every_kind_of_line_ending_is_accepted(string text)
    {
        var table = Read(text);

        table.Rows.Count.ShouldBe(2);
        table.Rows[1]["a"].ShouldBe("3");
        table.Rows[1]["b"].ShouldBe("4");
    }

    [Fact]
    public void Blank_lines_are_skipped_but_line_numbers_still_count_them()
    {
        var table = Read("a,b\n\n1,2\n\n\n3,4\n");

        table.Rows.Count.ShouldBe(2);
        table.Rows[0].LineNumber.ShouldBe(3);
        table.Rows[1].LineNumber.ShouldBe(6);
    }

    [Fact]
    public void A_column_that_does_not_exist_reads_as_empty_and_can_be_asked_about()
    {
        var row = Read("a,b\n1,2\n").Rows[0];

        row["nope"].ShouldBe(string.Empty);
        row.Has("a").ShouldBeTrue();
        row.Has("nope").ShouldBeFalse();
    }

    [Fact]
    public void A_short_row_reads_the_missing_fields_as_empty()
    {
        var row = Read("a,b,c\n1,2\n").Rows[0];

        row["b"].ShouldBe("2");
        row["c"].ShouldBe(string.Empty);
    }

    [Fact]
    public void A_long_row_ignores_the_extra_fields()
    {
        var row = Read("a,b\n1,2,3,4\n").Rows[0];

        row["a"].ShouldBe("1");
        row["b"].ShouldBe("2");
    }

    [Fact]
    public void A_file_with_only_a_header_has_no_rows()
    {
        Read("a,b\n").Rows.ShouldBeEmpty();
        Read("a,b").Rows.ShouldBeEmpty();
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_not_part_of_the_first_column()
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("ident,name\nSBCT,Curitiba\n"))
            .ToArray();

        var table = CsvTable.Read(new MemoryStream(bytes));

        table.Columns[0].ShouldBe("ident");
        table.Rows[0]["ident"].ShouldBe("SBCT");
    }

    [Fact]
    public void Accented_text_survives_when_read_from_a_stream()
    {
        var table = CsvTable.Read(new MemoryStream(Encoding.UTF8.GetBytes("name\nSão Gonçalo do Amarante\n")));

        table.Rows[0]["name"].ShouldBe("São Gonçalo do Amarante");
    }

    [Fact]
    public void A_quote_inside_an_unquoted_field_is_kept_as_text()
    {
        Read("a\nsay \"hi\"\n").Rows[0]["a"].ShouldBe("say \"hi\"");
    }

    [Fact]
    public void An_empty_file_is_an_error_because_there_is_no_header()
    {
        var error = Should.Throw<DataFormatException>(() => Read(string.Empty));

        error.Message.ShouldContain("header");
    }

    [Fact]
    public void A_quoted_field_that_never_closes_is_an_error_with_the_line_where_it_started()
    {
        var error = Should.Throw<DataFormatException>(() => Read("a,b\n1,2\n3,\"never closed\n4,5\n"));

        error.LineNumber.ShouldBe(3);
        error.Message.ShouldContain("line 3");
    }

    [Fact]
    public void Column_names_are_matched_exactly()
    {
        var row = Read("Ident\nSBCT\n").Rows[0];

        row["Ident"].ShouldBe("SBCT");
        row["ident"].ShouldBe(string.Empty);
    }
}
