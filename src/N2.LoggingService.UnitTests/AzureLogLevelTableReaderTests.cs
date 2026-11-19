namespace N2.LoggingService.UnitTests;

[TestClass]
public class AzureLogLevelTableReaderTests
{
    [TestMethod]
    public void SelectedPropertiesContainRowKeyAndLevel()
    {
        // Azure Tables returns only the listed properties when a select list is given,
        // so a missing RowKey made every level entry unusable.
        CollectionAssert.Contains(AzureLogLevelTableReader.SelectedProperties.ToList(), TableLoggingKeys.RowKeyProperty);
        CollectionAssert.Contains(AzureLogLevelTableReader.SelectedProperties.ToList(), TableLoggingKeys.LevelProperty);
    }

    [TestMethod]
    public void RowKeyPropertyMatchesTableEntityRowKey()
    {
        Assert.AreEqual(nameof(Azure.Data.Tables.TableEntity.RowKey), TableLoggingKeys.RowKeyProperty);
    }
}
