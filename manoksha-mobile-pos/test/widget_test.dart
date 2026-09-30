import 'package:flutter_test/flutter_test.dart';
import 'package:manoksha_pos/printing.dart';

void main() {
  test('rupee formatting', () {
    expect(rs(1234.5), 'Rs.1,234.50');
  });
}
