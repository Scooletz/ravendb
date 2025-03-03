import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { FormSelect } from "components/common/Form";
import { SelectOption } from "components/common/select/Select";

export default function SetupWizardSetupMethodStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { setupMethodStep } = useWatch({ control });

    return (
        <div>
            <FormSelect control={control} name="setupMethodStep.method" options={setupMethodOptions} />
        </div>
    );
}

const setupMethodOptions: SelectOption<SetupWizardFormData["setupMethodStep"]["method"]>[] = [
    { value: "newCluster", label: "New cluster" },
    { value: "createPackage", label: "Create package" },
    { value: "usePackage", label: "Use package" },
];
