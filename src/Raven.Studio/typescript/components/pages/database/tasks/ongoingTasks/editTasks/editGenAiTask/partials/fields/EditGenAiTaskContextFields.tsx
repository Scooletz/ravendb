import { FormLabel, FormSelectCreatable } from "components/common/Form";
import { FormGroup } from "components/common/Form";
import { FormAceEditor } from "components/common/Form";
import { useFormContext } from "react-hook-form";
import { EditGenAiTaskFormData } from "../../utils/editGenAiTaskValidation";
import { useAppSelector } from "components/store";
import { collectionsTrackerSelectors } from "components/common/shell/collectionsTrackerSlice";
import { SelectOption } from "components/common/select/Select";
import { useRef } from "react";
import ReactAce from "react-ace/lib/ace";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { Icon } from "components/common/Icon";
import AceEditor from "components/common/ace/AceEditor";

export default function EditGenAiTaskContextFields() {
    const { control, setValue } = useFormContext<EditGenAiTaskFormData>();

    const collectionOptions: SelectOption[] = useAppSelector(collectionsTrackerSelectors.collectionNames).map((x) => ({
        value: x,
        label: x,
    }));

    const scriptRef = useRef<ReactAce>(null);

    return (
        <>
            <FormGroup>
                <FormLabel>
                    Collection name
                    <PopoverWithHoverWrapper message="TODO">
                        <Icon icon="info" color="info" margin="ms-1" />
                    </PopoverWithHoverWrapper>
                </FormLabel>
                <FormSelectCreatable control={control} name="collectionName" options={collectionOptions} />
            </FormGroup>
            <FormGroup>
                <FormLabel>
                    Context extraction script
                    <PopoverWithHoverWrapper message="TODO">
                        <Icon icon="info" color="info" margin="ms-1" />
                    </PopoverWithHoverWrapper>
                </FormLabel>
                <FormAceEditor
                    aceRef={scriptRef}
                    control={control}
                    name="script"
                    mode="javascript"
                    actions={[
                        { component: <AceEditor.FullScreenAction /> },
                        { component: <AceEditor.FormatAction /> },
                        {
                            component: (
                                <AceEditor.LoadFileAction
                                    onLoad={(value) => setValue("script", value, { shouldValidate: true })}
                                />
                            ),
                        },
                    ]}
                />
            </FormGroup>
        </>
    );
}
