import classNames from "classnames";
import { Icon } from "components/common/Icon";
import IconName from "typings/server/icons";

interface SetupWizardClickableCardProps {
    icon: IconName;
    addon?: IconName;
    title: string;
    description: string;
    isSelected: boolean;
    onClick: () => void;
    className?: string;
    isDisabled?: boolean;
}

export default function SetupWizardClickableCard(props: SetupWizardClickableCardProps) {
    // TODO use PopoverWithHoverWrapper to 'When to use?'

    return (
        <div
            className={classNames(
                "border rounded p-4 cursor-pointer",
                props.className,
                {
                    "bg-faded-primary border-primary": props.isSelected,
                },
                {
                    "border-secondary": !props.isSelected,
                },
                {
                    "item-disabled pe-none": props.isDisabled,
                }
            )}
            onClick={props.onClick}
        >
            <div className="text-emphasis hstack gap-4">
                <div>
                    <Icon icon={props.icon} addon={props.addon} margin="m-0" style={{ fontSize: 24 }} />
                </div>
                <div className="flex-grow">
                    <h4 className="mb-0">{props.title}</h4>
                    <span>{props.description}</span>
                </div>
                <div>
                    <Icon icon="info" />
                    When to use?
                </div>
            </div>
        </div>
    );
}
