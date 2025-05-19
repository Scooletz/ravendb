import { FormLabel, FormSelectCreatable } from "components/common/Form";
import { FormGroup } from "components/common/Form";
import { FormAceEditor } from "components/common/Form";
import { useFormContext } from "react-hook-form";
import { EditGenAiTaskFormData } from "../../utils/editGenAiTaskValidation";
import { useAppSelector } from "components/store";
import { collectionsTrackerSelectors } from "components/common/shell/collectionsTrackerSlice";
import { SelectOption } from "components/common/select/Select";
import EditGenAiLoadFile from "../EditGenAiLoadFile";
import { useRef } from "react";
import ReactAce from "react-ace/lib/ace";
import Button from "react-bootstrap/Button";

import "ace-builds/src-noconflict/ext-beautify";
const beautify = ace.require("ace/ext/beautify").beautify;

export default function EditGenAiTaskContextFields() {
    const { control } = useFormContext<EditGenAiTaskFormData>();

    const collectionOptions: SelectOption[] = useAppSelector(collectionsTrackerSelectors.collectionNames).map((x) => ({
        value: x,
        label: x,
    }));

    const scriptRef = useRef<ReactAce>(null);

    return (
        <>
            <FormGroup>
                <FormLabel>Collection Name</FormLabel>
                <FormSelectCreatable control={control} name="collectionName" options={collectionOptions} />
            </FormGroup>
            <Button variant="primary" onClick={() => beautify(scriptRef.current?.editor.session)}>
                Format script
            </Button>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    Context extraction script
                    <EditGenAiLoadFile name="script" />
                </FormLabel>
                <FormAceEditor aceRef={scriptRef} control={control} name="script" mode="javascript" />
            </FormGroup>
        </>
    );
}
