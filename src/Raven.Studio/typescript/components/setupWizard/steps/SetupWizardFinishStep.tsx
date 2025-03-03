import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardFinishStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const formData = useWatch({ control });

    return <h1>Finish</h1>;
}
