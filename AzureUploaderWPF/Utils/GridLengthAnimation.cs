using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AzureUploaderWPF.Utils
{
    public class GridLengthAnimation : AnimationTimeline
    {
        private bool isCompleted;

        static GridLengthAnimation()
        {
            FromProperty = DependencyProperty.Register("From", typeof(GridLength),
                typeof(GridLengthAnimation));

            ToProperty = DependencyProperty.Register("To", typeof(GridLength),
                typeof(GridLengthAnimation));

            EasingFunctionProperty = DependencyProperty.Register("EasingFunction", 
                typeof(IEasingFunction), typeof(GridLengthAnimation));
        }

        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public static readonly DependencyProperty FromProperty;
        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public static readonly DependencyProperty ToProperty;
        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public static readonly DependencyProperty EasingFunctionProperty;
        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override object GetCurrentValue(object defaultOriginValue, 
                                              object defaultDestinationValue, 
                                              AnimationClock animationClock)
        {
            if (animationClock.CurrentProgress == null)
                return defaultOriginValue;

            double progress = animationClock.CurrentProgress.Value;
            
            if (EasingFunction != null)
                progress = EasingFunction.Ease(progress);

            var fromValue = From.IsAuto ? (GridLength)defaultOriginValue : From;
            var toValue = To.IsAuto ? (GridLength)defaultDestinationValue : To;

            if (fromValue.IsAuto)
                fromValue = new GridLength(0, toValue.IsStar ? GridUnitType.Star : GridUnitType.Pixel);
            
            if (toValue.IsAuto)
                toValue = new GridLength(0, fromValue.IsStar ? GridUnitType.Star : GridUnitType.Pixel);

            if (fromValue.GridUnitType != toValue.GridUnitType)
            {
                // Handle transition between different unit types if needed
                if (animationClock.CurrentProgress.Value >= 1)
                    return toValue;
                return fromValue;
            }

            var value = fromValue.Value + (toValue.Value - fromValue.Value) * progress;
            
            if (!isCompleted && progress >= 1.0)
            {
                isCompleted = true;
            }
            
            return new GridLength(value, fromValue.GridUnitType);
        }
    }
} 