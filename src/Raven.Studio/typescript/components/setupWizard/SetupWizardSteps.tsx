// import { useFormContext, useWatch } from "react-hook-form";
// import { SetupWizardFormData } from "./setupWizardValidation";
// import { NumberedList, NumberedListItem } from "components/common/NumberedList";
// import { ReactNode } from "react";

// export default function SetupWizardSteps() {
//     const { control } = useFormContext<SetupWizardFormData>();
//     const formValues = useWatch({ control });

//     const currentStep = formValues.currentStep;

//     return (
//         <div>
//             <NumberedList>
//                 <NumberedListRichItem></NumberedListRichItem>
//             </NumberedList>
//         </div>
//     );
// }

// interface NumberedListRichItemProps {
//     stepKey?: number | string;
//     children: ReactNode;
// }

// export function NumberedListRichItem(props: NumberedListRichItemProps) {
//     const { stepKey = 0, children } = props;

//     return (
//         <li className="numbered-list-item">
//             <span className="dot-number">{stepKey}</span>
//             {children}
//         </li>
//     );
// }
