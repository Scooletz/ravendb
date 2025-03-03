import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardSummaryStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const formData = useWatch({ control });

    return <h1>Summary</h1>;
}
