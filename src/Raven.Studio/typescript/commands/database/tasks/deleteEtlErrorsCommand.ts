import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

interface deleteEtlErrorsDto {
    name?: string[];
    shardNumber?: number
    nodeTag?: string
}
class deleteEtlErrorsCommand extends commandBase {
    constructor(private db: database | string, private deleteEtlDto?: deleteEtlErrorsDto) {
        super();
    }

    execute(): JQueryPromise<void> {
        const url = endpoints.databases.etl.etlErrors + this.urlEncodeArgs(this.deleteEtlDto);
        
        return this.del<void>(url, null, this.db, { dataType: "text" })
            .done(() => this.reportSuccess("ETL errors were deleted."))
            .fail((response: JQueryXHR) => this.reportError("Failed to delete ETL errors.", response.responseText, response.statusText));
    }
}

export = deleteEtlErrorsCommand;

